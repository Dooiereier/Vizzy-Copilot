namespace Assets.Scripts
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Assets.Scripts.Craft.FlightData;
    using Assets.Scripts.Craft.Parts.Modifiers;
    using ModApi.Craft;
    using ModApi.Craft.Program.Craft;
    using ModApi.Expressions;
    using UnityEngine;

    /// <summary>
    /// Diagnostic, off by default. Once per flight scene, compiles every Funk name through the game's
    /// own Vizzy path and evaluates it here, not in a program thread, so a failure is only logged and
    /// running programs carry on. It accepts the same result types as EvaluationExpression.
    /// </summary>
    public class FunkCheck : MonoBehaviour
    {
        private const float DelaySeconds = 3f;

        private float _startTime;
        private bool _done;
        private int _ok;
        private int _failed;

        private void Start()
        {
            _startTime = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            if (_done || !ModSettings.Instance.RunFunkCheck.Value || Time.realtimeSinceStartup - _startTime < DelaySeconds)
            {
                return;
            }

            var craft = Game.Instance.FlightScene?.CraftNode?.CraftScript;
            if (craft?.Data?.Assembly == null)
            {
                return;
            }

            _done = true;
            try
            {
                Run(craft);
            }
            catch (Exception e)
            {
                EditorLog.Error("Funk check stopped: " + e);
            }
        }

        private void Run(ICraftScript craft)
        {
            var data = CraftPrograms.All(craft).FirstOrDefault(program => program.Script?.FlightProgram != null);
            if (data == null)
            {
                EditorLog.Warning("Funk check needs a running Vizzy program on this craft.");
                return;
            }

            // Private on the script; it's what a program's own Funk blocks compile through.
            var service = (ICraftService)typeof(FlightProgramScript)
                .GetField("_craftService", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(data.Script);

            EditorLog.Info("Funk check starting. Values are from this moment of the flight.");
            foreach (var test in Tests(data))
            {
                Check(service, test);
            }

            EditorLog.Info("Funk check done: " + _ok + " OK, " + _failed + " failed.");
        }

        private static IEnumerable<string> Tests(FlightProgramData data)
        {
            var context = new Context(true);
            foreach (DictionaryEntry constant in FunkAutoFill.ContextMember(context, "Constants"))
            {
                yield return (string)constant.Key;
            }

            foreach (DictionaryEntry function in FunkAutoFill.ContextMember(context, "Functions"))
            {
                var method = (MethodInfo)function.Value.GetType().GetField("Item1").GetValue(function.Value);
                yield return function.Key + "(" + string.Join(", ", method.GetParameters().Select(SampleArgument)) + ")";
            }

            yield return "PID(1, 0, 1, 0, 0)";
            yield return "smooth(1, 1)";
            yield return "sum(1)";
            yield return "rate(1)";

            foreach (var name in typeof(CraftControls).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name))
            {
                yield return name;
            }

            foreach (var name in typeof(CraftFlightData).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name))
            {
                yield return "FD." + name;
            }

            foreach (var name in typeof(CraftOrbitData).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name))
            {
                yield return "OD." + name;
            }

            yield return "AG1";

            var variables = data.Script.FlightProgram.GlobalVariables.Variables;
            var part = data.Part.Name;
            var variable = variables.FirstOrDefault(item => !item.IsList);
            if (variable != null)
            {
                yield return variable.Name;
                yield return "v:" + variable.Name;
                yield return part + ".VZ." + variable.Name;
                yield return part + ".FlightProgram." + variable.Name;
                yield return part + ".VZ.v:" + variable.Name;
                yield return "[" + part + "].VZ." + variable.Name;
                yield return "{" + part + "}.VZ." + variable.Name;
                yield return part.Replace(" ", "_") + ".VZ." + variable.Name;
            }

            var list = variables.FirstOrDefault(item => item.IsList);
            if (list != null)
            {
                yield return "listLen(\"" + list.Name + "\")";
                yield return "listNum(\"" + list.Name + "\", 0)";
                yield return "listNum(\"" + list.Name + "\", 1)";
                yield return "listStr(\"" + list.Name + "\", 1)";
            }
        }

        private static string SampleArgument(ParameterInfo parameter)
        {
            return parameter.ParameterType == typeof(Vector3d) ? "vec(1, 2, 3)" : "1";
        }

        private void Check(ICraftService service, string text)
        {
            Delegate compiled = null;
            try
            {
                compiled = service.GetInputExpression(text);
                var (value, type) = Evaluate(compiled);
                _ok++;
                EditorLog.Info("Funk check OK: " + text + " = " + value + " (" + type + ")");
            }
            catch (Exception e)
            {
                _failed++;
                EditorLog.Info("Funk check FAIL: " + text + ": " + (e.InnerException ?? e).Message);
            }
            finally
            {
                if (compiled != null)
                {
                    try
                    {
                        service.ReleaseInputExpression(compiled);
                    }
                    catch (Exception e)
                    {
                        EditorLog.Debug("Funk check couldn't release " + text + ": " + e.Message);
                    }
                }
            }
        }

        private static (object value, string type) Evaluate(Delegate compiled)
        {
            switch (compiled)
            {
                case null:
                    throw new InvalidOperationException("didn't compile");
                case Func<double> number:
                    return (number(), "number");
                case Func<bool> boolean:
                    return (boolean(), "true/false");
                case Func<Vector3d> vector:
                    return (vector(), "vector");
                case Func<string> text:
                    return (text(), "text");
                default:
                    throw new InvalidOperationException("Vizzy can't use a result of type " + compiled.Method.ReturnType.Name);
            }
        }
    }
}
