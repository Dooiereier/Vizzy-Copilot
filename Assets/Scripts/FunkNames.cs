namespace Assets.Scripts
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Assets.Scripts.Craft.FlightData;
    using ModApi.Craft;
    using ModApi.Expressions;
    using UnityEngine;

    /// <summary>
    /// The names a Vizzy Funk field can use, read from the game so they can't drift from it. Auto-fill
    /// and the coloring's error marks share these. Only results Vizzy can use count: FD.AccelerationFrame
    /// compiles, then stops a running program.
    /// </summary>
    public static class FunkNames
    {
        private static readonly Dictionary<string, string> KnownSignatures = new Dictionary<string, string>
        {
            // The stateful functions and Vizzy's list functions have no parameter names to read.
            { "PID", "PID(target, current, p, i, d)" },
            { "smooth", "smooth(value, maxRate)" },
            { "sum", "sum(value)" },
            { "rate", "rate(value)" },
            { "listLen", "listLen(\"name\")" },
            { "listNum", "listNum(\"name\", index)" },
            { "listBool", "listBool(\"name\", index)" },
            { "listStr", "listStr(\"name\", index)" },
            { "listVec", "listVec(\"name\", index)" },
        };

        private static List<FunkName> _functions;
        private static List<FunkName> _constants;
        private static List<FunkName> _controls;
        private static List<FunkName> _flightData;
        private static List<FunkName> _orbitData;

        public static List<FunkName> Functions => _functions ?? (_functions = ReadFunctions());

        public static List<FunkName> Constants => _constants ?? (_constants = ReadConstants());

        public static List<FunkName> Controls => _controls ?? (_controls = ValueProperties(typeof(CraftControls)));

        public static List<FunkName> FlightData => _flightData ?? (_flightData = ValueProperties(typeof(CraftFlightData)));

        public static List<FunkName> OrbitData => _orbitData ?? (_orbitData = ValueProperties(typeof(CraftOrbitData)));

        /// <summary>Non-public in the game's Context; reading them keeps these lists matching the game's.</summary>
        public static IDictionary ContextMember(Context context, string name)
        {
            return (IDictionary)typeof(Context)
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .GetValue(context);
        }

        private static List<FunkName> ReadFunctions()
        {
            var names = new List<FunkName>();
            foreach (DictionaryEntry function in ContextMember(new Context(true), "Functions"))
            {
                var method = (MethodInfo)function.Value.GetType().GetField("Item1").GetValue(function.Value);
                var parameters = method.GetParameters().Select(parameter => parameter.Name);
                var name = (string)function.Key;
                if (!KnownSignatures.ContainsKey(name))
                {
                    names.Add(new FunkName(name, name + "(" + string.Join(", ", parameters) + ")"));
                }
            }

            names.AddRange(KnownSignatures.Select(known => new FunkName(known.Key, known.Value)));
            return names;
        }

        private static List<FunkName> ReadConstants()
        {
            var names = new List<FunkName>();
            foreach (DictionaryEntry constant in ContextMember(new Context(true), "Constants"))
            {
                names.Add(new FunkName((string)constant.Key));
            }

            return names;
        }

        private static List<FunkName> ValueProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => IsUsableInVizzy(property.PropertyType))
                .Select(property => new FunkName(property.Name))
                .ToList();
        }

        private static bool IsUsableInVizzy(Type type)
        {
            return type == typeof(float) || type == typeof(double) || type == typeof(bool) || type == typeof(int) || type == typeof(short)
                || type == typeof(Vector3d);
        }
    }
}
