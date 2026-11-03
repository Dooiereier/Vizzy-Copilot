using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Craft.Parts.Modifiers;
using Assets.Scripts.Vizzy.UI;
using ModApi.Craft.Parts;
using ModApi.Craft.Program;
using ModApi.Craft.Program.Expressions;
using UnityEngine;

namespace Assets.Scripts.Vizzy
{
    /// <summary>
    /// 清理 Vizzy 程序中未被引用的全局变量。
    /// </summary>
    public static class VizzyVariableCleaner
    {
        /// <summary>
        /// 删除 FlightProgram 中未被任何节点引用的全局变量，返回被删除的变量名列表。
        /// 只操作定义层（FlightProgram.GlobalVariables，即 Vizzy 编辑器里管理的变量）。
        /// 注意：程序正在运行时，运行中的 Process 持有全局变量副本，需重新启动程序
        /// （调用 FlightProgramScript.StartProgram）后才会反映清理结果。
        /// </summary>

        /// <summary>
        /// 计算 FlightProgram 中未被任何节点引用的全局变量名（只统计，不修改）。
        /// </summary>
        public static IReadOnlyList<string> GetUnusedVariableNames(FlightProgram program)
        {
            var unused = new List<string>();
            if (program == null || program.GlobalVariables == null)
            {
                return unused;
            }

            // 收集整个程序里被引用过的全局变量名（含指令树、表达式、自定义指令/表达式）
            var used = new HashSet<string>(System.StringComparer.Ordinal);
            CollectVariables(program.RootInstructions, used);
            CollectVariables(program.RootExpressions, used);
            CollectVariables(program.CustomInstructions, used);
            CollectVariables(program.CustomExpressions, used);

            foreach (var variable in program.GlobalVariables.Variables)
            {
                if (!used.Contains(variable.Name))
                {
                    unused.Add(variable.Name);
                }
            }
            return unused;
        }

        /// <summary>
        /// 清理当前正在编辑的 Vizzy 程序（devConsole 命令直接调这个即可）。
        /// 通过场景中存活的 VizzyUIScript 拿到当前编辑的程序，并对每个未使用变量走
        /// 游戏原生 VizzyUIScript.DeleteVariable 路径 —— 会同时移除变量面板里的 UI 元素。
        /// 返回被删除的变量名列表。
        /// </summary>
        public static void CleanCurrentEditingProgram()
        {
            var ui = Object.FindObjectOfType<VizzyUIScript>();
            if (ui == null || ui.FlightProgram == null)
            {
                return;
            }
            
            foreach (var name in GetUnusedVariableNames(ui.FlightProgram))
            {
                ui.DeleteVariable(name);
            }
        }
        
        

        private static void CollectVariables(IEnumerable<ProgramNode> roots, HashSet<string> used)
        {
            foreach (var root in roots)
            {
                ProgramNode.ExecuteActionOnTree<VariableExpression>(root, variableExpression =>
                {
                    // 只统计全局变量引用（本地变量不在 GlobalVariables 里）
                    if (!variableExpression.IsLocal && !string.IsNullOrEmpty(variableExpression.VariableName))
                    {
                        used.Add(variableExpression.VariableName);
                    }
                });
            }
        }
    }
}
