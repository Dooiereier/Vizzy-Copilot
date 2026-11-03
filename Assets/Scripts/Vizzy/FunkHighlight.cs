using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Assets.Scripts.Vizzy.UI;
using Assets.Scripts.Vizzy.UI.Elements;
using HarmonyLib;
using ModApi.Craft.Program;
using ModApi.Craft.Program.Expressions;
using ModApi.Craft.Program.Instructions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.Vizzy
{
    /// <summary>
    /// Funk 表达式语法高亮（TMP 逐字符顶点色方案）。
    ///
    /// 原理：不往文本里插入任何富文本标记，直接修改 TMP_Text 的 mesh 顶点色
    /// （textInfo.meshInfo[i].colors32）。textComponent.text 始终是纯文本，
    /// caret 定位（用 characterInfo）与 selection 高亮（独立 VertexHelper 几何体）
    /// 完全不受影响；数据层 input.text / ExpressionResult.TextValue 永远是纯文本。
    ///
    /// 注入时机：订阅 TMP_Text.OnPreRenderText 事件。该事件在 GenerateTextMesh 内部、
    /// 上传 mesh 数据（m_mesh.colors32 = meshInfo[0].colors32）之前触发，因此此刻
    /// 修改 meshInfo.colors32 会被随后上传，颜色一定生效（输入、显示、拖动重排都覆盖）。
    ///
    /// 映射：块 Start 时（[HarmonyPatch] Start Postfix）把 textComponent.GetInstanceID()
    /// 注册进字典并订阅事件，不依赖 GetComponentInParent 层级反查。
    /// </summary>
    public static class FunkHighlight
    {
        // ---- 词法规则：与 ModApi.Expressions.Parser.TokenDefs 一致（保留 \G 锚定）----
        private static readonly Regex WhitespacePat = new Regex(@"\G\s*");
        private static readonly Regex StringPat = new Regex(@"\G""((?:[^\\""]|\\.)*)""");
        private static readonly Regex DataSlotPat = new Regex(@"\G\[[^\]]+\]");
        private static readonly Regex NumberPat = new Regex(@"\G[0-9]*\.?[0-9]+([eE][-+]?[0-9]+)?");
        private static readonly Regex NamePat = new Regex(@"\G(?:v:)?[A-z_\.\$][A-z_0-9\.]*");
        private static readonly Regex OperatorPat = new Regex(@"\G(?:(?:[<>!]=?)|[+\-/*&|=%?:])");
        private static readonly Regex SeparatorPat = new Regex(@"\G,");
        private static readonly Regex GroupPat = new Regex(@"\G[()]");

        
        private static readonly Color32 DefaultColor = new Color32(255, 255, 255, 255);
        private static readonly Color32 ColorNumber = FromHex("#B5CEA8");   // 数字 绿
        private static readonly Color32 ColorString = FromHex("#CE9178");   // 字符串 橙
        private static readonly Color32 ColorOperator = FromHex("#D4D4D4"); // 操作符 白灰
        private static readonly Color32 ColorFunction = FromHex("#DCDCAA"); // 函数 黄
        private static readonly Color32 ColorKeyword = FromHex("#569CD6");  // 关键字 蓝
        private static readonly Color32 ColorVariable = FromHex("#9CDCFE"); // 变量 浅蓝
        private static readonly Color32 ColorPart = FromHex("#808080");     // PartSelector 灰
        private static readonly Color32 ColorDataSlot = FromHex("#4EC9B0"); // 数据槽 青
        private static readonly Color32 ColorGroup = FromHex("#C586C0");    // 括号 紫
        private static readonly Color32 ColorVector = FromHex("#4EC9B0");   // v: 向量前缀 青
        private static readonly Color32 ColorError = FromHex("#FF4D4D");    // 语法错误 红

        private static readonly HashSet<string> Functions = new HashSet<string>(StringComparer.Ordinal)
        {
            // Math?
            "abs", "ceil", "clamp", "clamp01", "deltaAngle", "exp", "floor", "inverselerp", "lerp",
            "lerpangle", "lerpunclamped", "log", "log10", "pingpong", "max", "min", "pow", "repeat",
            "round", "sign", "smoothstep", "sqrt", "sin", "cos", "tan", "asin", "acos", "atan", "atan2",
            // 向量函数
            "angle", "clampMagnitude", "cross", "distance", "dot", "exclude", "isNaN", "lerp3",
            "magnitude", "max3", "min3", "moveTowards", "normalize", "project", "projectOnPlane",
            "reflect", "scale", "signedAngle", "slerp", "sqrMagnitude", "vec", "x", "y", "z",
            // 列表检索器
            "listNum", "listVec", "listBool", "listStr", "listLen",
            // some extra stuff
            "ln", "sum", "rate", "PID", "smooth"
        };

        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "pi", "E", "Rad2Deg", "Deg2Rad", "true", "false"
        };

        // Controls 裸属性（CraftControls 属性，游戏 ResolveVariable 里直接 GetProperty(text) 注册）。
        private static readonly HashSet<string> ControlsProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "Brake", "EvaAnalogJump", "EvaMoveFwdAft", "EvaMoveUpDown", "EvaPitch", "EvaRoll",
            "EvaShootTether", "EvaStrafe", "EvaTetherLength", "EvaTetherLengthOffset", "EvaTurn", "EvaWalk",
            "OffsetBrake", "OffsetPitch", "OffsetRoll", "OffsetSlider1", "OffsetSlider2", "OffsetSlider3",
            "OffsetSlider4", "OffsetTranslateForward", "OffsetTranslateRight", "OffsetTranslateUp", "OffsetYaw",
            "Pitch", "PitchInputReceived", "Roll", "RollInputReceived", "Slider1", "Slider2", "Slider3",
            "Slider4", "Throttle", "TranslateForward", "TranslateRight", "TranslateUp", "TranslationModeEnabled",
            "Yaw", "YawInputReceived"
        };

        // 函数参数个数表（从游戏 Context.GetDefaults / Mathd / CraftService 提取，键统一小写）。
        // 只对表内已知函数做参数个数校验；sum/rate/PID/smooth 等特殊函数与自定义函数不校验。
        private static readonly Dictionary<string, int> FunctionArgCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // Math / Mathd 标量函数
            { "abs", 1 }, { "acos", 1 }, { "asin", 1 }, { "atan", 1 }, { "atan2", 2 },
            { "cos", 1 }, { "sin", 1 }, { "tan", 1 }, { "log", 2 }, { "ln", 1 }, { "log10", 1 },
            { "max", 2 }, { "min", 2 }, { "pow", 2 }, { "sqrt", 1 }, { "exp", 1 }, { "ceil", 1 }, { "floor", 1 },
            { "clamp", 3 }, { "clamp01", 1 }, { "deltaangle", 2 }, { "inverselerp", 3 },
            { "lerp", 3 }, { "lerpangle", 3 }, { "lerpunclamped", 3 }, { "pingpong", 2 },
            { "repeat", 2 }, { "round", 1 }, { "sign", 1 }, { "smoothstep", 3 },
            // 向量构造/提取
            { "vec", 3 }, { "x", 1 }, { "y", 1 }, { "z", 1 },
            // Vector3d 静态函数
            { "angle", 2 }, { "clampmagnitude", 2 }, { "cross", 2 }, { "distance", 2 }, { "dot", 2 },
            { "exclude", 2 }, { "isnan", 1 }, { "lerp3", 3 }, { "magnitude", 1 }, { "max3", 2 },
            { "min3", 2 }, { "movetowards", 3 }, { "normalize", 1 }, { "project", 2 }, { "projectonplane", 2 },
            { "reflect", 2 }, { "scale", 2 }, { "signedangle", 3 }, { "slerp", 3 }, { "sqrmagnitude", 1 },
            // 列表检索器
            { "listnum", 2 }, { "listvec", 2 }, { "listbool", 2 }, { "liststr", 2 }, { "listlen", 1 }
        };

        // FlightData 全部属性名（JNO XML Guide 社区指南 + 游戏 ICraftFlightData 提取）。
        // Funk 表达式里 FlightData.X / FD.X 的属性名必须在此表中。
        private static readonly HashSet<string> FlightDataProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "Acceleration", "AccelerationMagnitude", "AltitudeAboveGroundLevel", "AltitudeAboveSeaLevel",
            "AltitudeAboveTerrain", "AngleOfAttack", "AngularVelocity", "AngularVelocityMagnitude",
            "BankAngle", "CraftForward", "CraftRight", "CraftUp", "CurrentEngineThrust",
            "CurrentEngineThrustUnscaled", "CurrentMass", "CurrentMassUnscaled", "CurrentReactionControlNozzleThrust",
            "DeltaVStage", "DragAccelerationMagnitude", "East", "FuelMass", "Gravity", "GravityMagnitude",
            "Grounded", "Heading", "InWater", "LateralSurfaceVelocity", "MachNumber", "MaxActiveEngineThrust",
            "MaxActiveEngineThrustUnscaled", "North", "ParentPlanetOcclusion", "Pitch", "Position",
            "PositionNormalized", "RemainingBattery", "RemainingFuelInStage", "RemainingMonopropellant",
            "SideSlip", "SolarRadiationDirection", "SolarRadiationIntensity", "SupportsWarpBurn",
            "SurfaceVelocity", "SurfaceVelocityMagnitude", "TimeDelta", "TimeMultiplier", "TimeReal",
            "Velocity", "VelocityMagnitude", "VerticalSurfaceVelocity", "WeightedThrottleResponse",
            "WeightedThrottleResponseTime", "Orbit"
        };

        // OrbitData 全部属性名（JNO XML Guide 社区指南 + 游戏 ICraftOrbitData 提取）。
        // 支持OrbitData.X 全称，以及 FlightData.Orbit.X 子对象链。
        private static readonly HashSet<string> OrbitDataProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "ApoapsisAltitude", "ApoapsisTime", "BurnNodeDeltaV", "Eccentricity", "Inclination",
            "PeriapsisAltitude", "PeriapsisTime", "Period", "MeanAnomaly", "MeanMotion",
            "PeriapsisArgument", "RightAscension", "TrueAnomaly", "SemiMajorAxis", "SemiMinorAxis"
        };

        // textComponent.GetInstanceID() -> 所属 TMP_InputField
        private static readonly Dictionary<int, TMP_InputField> _textToInput = new Dictionary<int, TMP_InputField>();

        // textComponent.GetInstanceID() -> 错误波浪线 overlay（textComponent 的子物体）
        private static readonly Dictionary<int, TextMeshProUGUI> _errorOverlays = new Dictionary<int, TextMeshProUGUI>();

        private static readonly HashSet<string> _loggedOnce = new HashSet<string>(StringComparer.Ordinal);

        // 变量名校验缓存：当前飞行程序实例 + 0.5s 时间戳（避免每帧遍历程序树）
        private static FlightProgram _varCacheFp;
        private static HashSet<string> _varCacheNames;
        private static float _varCacheTime;

        /// <summary>
        /// 把 Funk 输入框的 textComponent 注册进字典并订阅 OnPreRenderText（块 Start 时调用）。
        /// 只处理真正的 Funk 表达式块；注释块等其他 TextInputElementScript 不订阅、不处理。
        /// </summary>
        public static void Register(TMP_InputField input)
        {
            if (input == null)
            {
                return;
            }
            TextInputElementScript block = input.GetComponent<TextInputElementScript>();
            if (block == null)
            {
                return;
            }
            // 诊断日志：打印每个文本输入块（含注释块）的类型信息，用于定位误处理
            try
            {
                string parentNode = block.Parent != null && block.Parent.Node != null
                    ? block.Parent.Node.GetType().Name : "null";
                Debug.Log("[FunkHighlight] Register: block='" + block.name + "' node="
                    + (block.Node != null ? block.Node.GetType().Name : "null")
                    + " parentNode=" + parentNode
                    + " style=" + (block.Style != null ? block.Style.Id : "null")
                    + " tokenType=" + (block.Token != null ? block.Token.TokenType.ToString() : "null")
                    + " validation='" + (block.Token != null ? block.Token.Validation : "null") + "'"
                    + " -> funk=" + IsFunkExpressionBlock(block));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FunkHighlight] Register log: " + e.Message);
            }
            if (!IsFunkExpressionBlock(block))
            {
                return; // 非 Funk 块（注释等）：不订阅、不受影响
            }
            TMP_Text tc = input.textComponent;
            if (tc == null)
            {
                return;
            }
            _textToInput[tc.GetInstanceID()] = input;
            // 防重复订阅（块可能多次 Start / 重建）
            tc.OnPreRenderText -= OnBeforeMeshUpload;
            tc.OnPreRenderText += OnBeforeMeshUpload;
        }

        /// <summary>
        /// 判断该块是否为 Funk 表达式块：Token 是 Input 槽且没有校验、节点是 ConstantExpression
        /// （Funk 自由表达式）。注释块（CommentInstruction）等指令内的文本输入不是
        /// ConstantExpression，会被精确排除，不受高亮/报错影响。
        /// </summary>
        public static bool IsFunkExpressionBlock(TextInputElementScript block)
        {
            if (block == null || !IsHighlightEnabled())
            {
                return false;
            }
            NodeFormat.Token token = block.Token;
            if (token == null
                || token.TokenType != NodeFormat.TokenType.Input
                || !string.IsNullOrEmpty(token.Validation))
            {
                return false;
            }
            // 真正的 Funk 表达式槽：父节点是 EvaluationExpression（游戏在
            // EvaluationExpression.Evaluate 里用 GetInputExpression 编译该槽文本）。
            // display/log/广播/partId 等指令的字符串输入、注释块（CommentInstruction）
            // 等父节点都不是 EvaluationExpression，会被精确排除。
            return block.Parent != null && block.Parent.Node is EvaluationExpression;
        }

        /// <summary>
        /// 收集当前飞行程序已定义的变量名（全局变量 + 程序树里所有 IsDefinition 的变量），
        /// 带 0.5s 缓存；拿不到飞行程序时返回 null（跳过变量校验，不误报）。
        /// </summary>
        private static HashSet<string> GetVariableNames(TextInputElementScript block)
        {
            if (block == null)
            {
                return null;
            }
            FlightProgram fp = null;
            try
            {
                IVizzyUI vizzy = block.VizzyUI;
                if (vizzy != null)
                {
                    fp = vizzy.FlightProgram;
                }
            }
            catch
            {
                return null;
            }
            if (fp == null)
            {
                _varCacheFp = null;
                _varCacheNames = null;
                return null;
            }
            if (_varCacheFp == fp && Time.time - _varCacheTime < 0.5f)
            {
                return _varCacheNames;
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (fp.GlobalVariables != null)
                {
                    foreach (Variable v in fp.GlobalVariables.Variables)
                    {
                        names.Add(v.Name);
                    }
                }
                CollectVariableDefinitions(fp.RootExpressions, names);
                CollectVariableDefinitions(fp.RootInstructions, names);
                CollectVariableDefinitions(fp.CustomExpressions, names);
                CollectVariableDefinitions(fp.CustomInstructions, names);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FunkHighlight] GetVariableNames: " + e.Message);
            }
            _varCacheFp = fp;
            _varCacheNames = names;
            _varCacheTime = Time.time;
            return names;
        }

        private static void CollectVariableDefinitions(IEnumerable<ProgramNode> nodes, HashSet<string> names)
        {
            if (nodes == null)
            {
                return;
            }
            foreach (ProgramNode node in nodes)
            {
                ProgramNode.ExecuteActionOnTree<VariableExpression>(node,
                    v => { if (v.IsDefinition) { names.Add(v.VariableName); } });
            }
        }

        private static bool IsHighlightEnabled()
        {
            try
            {
                return ModSettings.Instance.FunkSyntaxHighlight.Value;
            }
            catch
            {
                return true;
            }
        }

        // ---- 渲染入口：OnPreRenderText 事件（TMP 上传 mesh 数据之前触发）----

        /// <summary>TMP 即将把 meshInfo 上传到 m_mesh 时回调：此刻改 colors32 会被上传，颜色一定生效。</summary>
        public static void OnBeforeMeshUpload(TMP_TextInfo textInfo)
        {
            try
            {
                if (textInfo == null || textInfo.textComponent == null)
                {
                    return;
                }
                TMP_Text textComponent = textInfo.textComponent;
                TMP_InputField input = null;
                if (!_textToInput.TryGetValue(textComponent.GetInstanceID(), out input) || input == null)
                {
                    return; // 未注册（非 Funk 输入框的文本，正常跳过）
                }
                TextInputElementScript block = input.GetComponent<TextInputElementScript>();
                if (!IsFunkExpressionBlock(block))
                {
                    return;
                }
                LogOnce("[FunkHighlight] Upload: tc='" + textComponent.name + "' id=" + textComponent.GetInstanceID()
                    + " node=" + (block.Node != null ? block.Node.GetType().Name : "?")
                    + " parent=" + (block.Parent != null && block.Parent.Node != null ? block.Parent.Node.GetType().Name : "?")
                    + " style=" + (block.Style != null ? block.Style.Id : "?")
                    + " text='" + input.text + "'");
                HashSet<string> variableNames = GetVariableNames(block);
                ApplyMeshColors(input, textComponent, variableNames);
                LogOnce("[FunkHighlight] PR colored '" + input.text + "' chars=" + textComponent.textInfo.characterCount);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FunkHighlight] OnPreRenderText: " + e.Message);
            }
        }

        /// <summary>对 textComponent 的 meshInfo.colors32 按 token 逐字符填色（不标脏、不重建）。</summary>
        private static void ApplyMeshColors(TMP_InputField input, TMP_Text textComponent, HashSet<string> variableNames)
        {
            TMP_TextInfo textInfo = textComponent.textInfo;
            if (textInfo == null || textInfo.characterCount == 0)
            {
                return;
            }
            string text = input.text;
            int errStart, errEnd;
            string errMsg;
            bool hasError = FindError(text, out errStart, out errEnd, out errMsg, variableNames);
            if (hasError)
            {
                LogOnce("[FunkHighlight] ERR '" + text + "': " + errMsg + " (pos " + errStart + "-" + errEnd + ")");
            }
            Color32[] perChar = TokenizeToColors(text, errStart, errEnd);

            // 错误区间可见字符的屏幕区间（用于红色波浪线 overlay）
            bool errFound = false;
            float errX0 = 0f, errX1 = 0f, errY = 0f;

            for (int i = 0; i < textInfo.characterCount; i++)
            {
                TMP_CharacterInfo ch = textInfo.characterInfo[i];
                // 跳过不可见字符（空格、\u200B、换行等）：TMP 不为它们分配顶点，
                // vertexIndex 会与相邻可见字符共享，后写的不可见字符会覆盖可见字符颜色。
                if (!ch.isVisible)
                {
                    continue;
                }
                int mat = ch.materialReferenceIndex;
                if (mat < 0 || mat >= textInfo.meshInfo.Length)
                {
                    continue;
                }
                Color32[] colors32 = textInfo.meshInfo[mat].colors32;
                int vi = ch.vertexIndex;
                if (vi < 0 || vi + 3 >= colors32.Length)
                {
                    continue;
                }
                Color32 c = ch.index >= 0 && ch.index < perChar.Length ? perChar[ch.index] : DefaultColor;
                colors32[vi] = c;
                colors32[vi + 1] = c;
                colors32[vi + 2] = c;
                colors32[vi + 3] = c;

                // 收集错误区间坐标（基于源字符串索引）
                if (hasError && ch.index >= errStart && ch.index < errEnd)
                {
                    if (!errFound)
                    {
                        errX0 = ch.origin;
                        errY = ch.baseLine;
                        errFound = true;
                    }
                    // 跨行保护：错误区间跨多行时只统计第一个错误行的水平范围（波浪线只画该行）
                    if (Mathf.Abs(ch.baseLine - errY) < textComponent.fontSize * 0.5f)
                    {
                        errX1 = ch.xAdvance;
                    }
                }
            }

            // 错误波浪线 overlay：错误区间下方显示红色 ~ 波浪线
            if (hasError && errFound)
            {
                TextMeshProUGUI overlay = GetErrorOverlay(textComponent);
                RectTransform rt = overlay.rectTransform;
                float width = Mathf.Max(4f, errX1 - errX0);
                // 波浪线宽度不超过 textComponent 可见宽度，避免 ~ 溢出到相邻内容
                float maxW = textComponent.rectTransform.rect.width;
                if (width > maxW)
                {
                    width = maxW;
                }
                float offset = Mathf.Max(2f, textComponent.fontSize * 0.12f);
                rt.localPosition = new Vector3(errX0, errY - offset, 0f);
                rt.sizeDelta = new Vector2(width, Mathf.Max(6f, textComponent.fontSize * 0.3f));
                float approxCharW = Mathf.Max(2f, overlay.fontSize * 0.6f);
                int n = Mathf.Clamp(Mathf.RoundToInt(width / approxCharW), 2, 200);
                overlay.text = new string('~', n);
                overlay.enabled = true;
                LogOnce("[FunkHighlight] Overlay: text='" + input.text + "' err=[" + errStart + "," + errEnd
                    + "] x=[" + errX0.ToString("F1") + "," + errX1.ToString("F1") + "] y=" + errY.ToString("F1")
                    + " w=" + width.ToString("F1") + " n=" + n
                    + " pivot=" + textComponent.rectTransform.pivot
                    + " fontSize=" + textComponent.fontSize.ToString("F1"));
            }
            else
            {
                HideErrorOverlay(textComponent);
            }
        }

        // ---- 错误波浪线 overlay ----

        /// <summary>取（或创建）textComponent 下的红色波浪线 overlay（不参与布局、不挡点击）。</summary>
        private static TextMeshProUGUI GetErrorOverlay(TMP_Text textComponent)
        {
            int id = textComponent.GetInstanceID();
            TextMeshProUGUI overlay;
            if (_errorOverlays.TryGetValue(id, out overlay) && overlay != null)
            {
                return overlay;
            }
            var go = new GameObject("FunkErrorOverlay", typeof(RectTransform));
            go.transform.SetParent(textComponent.transform, false);
            overlay = go.AddComponent<TextMeshProUGUI>();
            overlay.font = textComponent.font;
            overlay.fontSize = Mathf.Max(8f, textComponent.fontSize * 0.7f);
            overlay.color = new Color32(255, 77, 77, 220);
            overlay.raycastTarget = false;
            overlay.richText = false;
            overlay.alignment = TextAlignmentOptions.Left;
            // 关键：禁止波浪线文本换行（~ 默认跟随全局 word wrap，超宽会折成多行 -> 与文本重叠）
            overlay.enableWordWrapping = false;
            overlay.text = "";
            overlay.enabled = false;
            // 不参与 LayoutGroup 布局
            var le = overlay.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            RectTransform rt = overlay.rectTransform;
            rt.pivot = new Vector2(0f, 0f);
            // anchor 对齐到 textComponent 的 pivot：TMP 字符坐标（origin/baseLine）是相对
            // textComponent pivot 的，overlay 的 localPosition 相对 anchor，取 pivot 即对齐。
            Vector2 pv = textComponent.rectTransform.pivot;
            rt.anchorMin = rt.anchorMax = pv;
            rt.localPosition = Vector3.zero;
            rt.sizeDelta = new Vector2(0f, 0f);
            _errorOverlays[id] = overlay;
            return overlay;
        }

        private static void HideErrorOverlay(TMP_Text textComponent)
        {
            TextMeshProUGUI overlay;
            if (_errorOverlays.TryGetValue(textComponent.GetInstanceID(), out overlay) && overlay != null)
            {
                overlay.enabled = false;
            }
        }

        // ---- 分词 ----

        private static Color32[] TokenizeToColors(string text, int errStart, int errEnd)
        {
            var colors = new Color32[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                colors[i] = DefaultColor;
            }
            int pos = 0;
            while (pos < text.Length)
            {
                Match ws = WhitespacePat.Match(text, pos);
                if (ws.Success && ws.Index == pos && ws.Length > 0)
                {
                    pos += ws.Length;
                    continue;
                }
                if (TryToken(text, ref pos, colors, StringPat, ColorString)) continue;
                if (TryToken(text, ref pos, colors, DataSlotPat, ColorDataSlot)) continue;
                if (TryToken(text, ref pos, colors, NumberPat, ColorNumber)) continue;

                Match name = NamePat.Match(text, pos);
                if (name.Success && name.Index == pos)
                {
                    Fill(colors, pos, name.Length, KindOfName(name.Value));
                    pos += name.Length;
                    continue;
                }

                if (TryToken(text, ref pos, colors, OperatorPat, ColorOperator)) continue;
                if (TryToken(text, ref pos, colors, SeparatorPat, ColorOperator)) continue;
                if (TryToken(text, ref pos, colors, GroupPat, ColorGroup)) continue;

                pos++;
            }
            // 错误区间覆盖为红色（在 token 色之上，保证错误显眼）
            if (errStart >= 0 && errStart < text.Length)
            {
                int end = errEnd < text.Length ? errEnd : text.Length;
                Fill(colors, errStart, end - errStart, ColorError);
            }
            return colors;
        }

        /// <summary>
        /// 语法错误检测：与 ModApi.Expressions.Parser 相同的词法规则与顺序。
        /// 分两层：词法层（无法识别的字符）+ token 流校验层（缺操作符、连续操作符、
        /// 逗号/括号位置错误、未闭合括号、表达式不完整等），错误位置精确到字符。
        /// </summary>
        private static bool IsActionGroupName(string name)
        {
            // 游戏 ResolveVariable：text.StartsWith("AG") 且剩余部分可解析为 int（AG1/AG2/...）。
            if (name.Length < 3 || name[0] != 'A' || name[1] != 'G')
            {
                return false;
            }
            for (int k = 2; k < name.Length; k++)
            {
                if (name[k] < '0' || name[k] > '9')
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>tokens[i] 是否可作为表达式开头（值 / 左括号 / 一元 +/-）。</summary>
        private static bool IsExpressionStart(List<FunkToken> tokens, int i)
        {
            if (i >= tokens.Count)
            {
                return false;
            }
            switch (tokens[i].kind)
            {
                case TokKind.Value:
                case TokKind.LParen:
                    return true;
                case TokKind.Op:
                    return tokens[i].value == "-" || tokens[i].value == "+" || tokens[i].value == "!";
                default:
                    return false;
            }
        }

        private static bool FindError(string text, out int errStart, out int errEnd, out string message, HashSet<string> variableNames = null)
        {
            errStart = -1;
            errEnd = -1;
            message = null;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            // ---- 第 1 层：词法扫描 + 收集 token ----
            var tokens = new List<FunkToken>();
            int pos = 0;
            int depth = 0;
            bool endsWithBadToken = false;
            int badTokenPos = 0;
            while (pos < text.Length)
            {
                Match ws = WhitespacePat.Match(text, pos);
                if (ws.Success && ws.Index == pos && ws.Length > 0)
                {
                    pos += ws.Length;
                    continue;
                }
                Match m = StringPat.Match(text, pos);
                if (m.Success && m.Index == pos) { tokens.Add(new FunkToken(pos, m.Length, TokKind.Value, m.Value, false, false, depth)); endsWithBadToken = false; pos += m.Length; continue; }
                m = DataSlotPat.Match(text, pos);
                if (m.Success && m.Index == pos) { tokens.Add(new FunkToken(pos, m.Length, TokKind.Value, m.Value, false, true, depth)); endsWithBadToken = false; pos += m.Length; continue; }
                m = NumberPat.Match(text, pos);
                if (m.Success && m.Index == pos) { tokens.Add(new FunkToken(pos, m.Length, TokKind.Value, m.Value, false, false, depth)); endsWithBadToken = false; pos += m.Length; continue; }
                m = NamePat.Match(text, pos);
                if (m.Success && m.Index == pos) { tokens.Add(new FunkToken(pos, m.Length, TokKind.Value, m.Value, true, false, depth)); endsWithBadToken = false; pos += m.Length; continue; }
                m = OperatorPat.Match(text, pos);
                if (m.Success && m.Index == pos) { tokens.Add(new FunkToken(pos, m.Length, TokKind.Op, m.Value, false, false, depth)); endsWithBadToken = true; badTokenPos = pos; pos += m.Length; continue; }
                m = SeparatorPat.Match(text, pos);
                if (m.Success && m.Index == pos) { tokens.Add(new FunkToken(pos, m.Length, TokKind.Comma, ",", false, false, depth)); endsWithBadToken = true; badTokenPos = pos; pos += m.Length; continue; }
                m = GroupPat.Match(text, pos);
                if (m.Success && m.Index == pos)
                {
                    if (m.Value == "(")
                    {
                        tokens.Add(new FunkToken(pos, m.Length, TokKind.LParen, "(", false, false, depth));
                        depth++;
                    }
                    else
                    {
                        if (depth == 0)
                        {
                            errStart = pos;
                            errEnd = pos + 1;
                            message = "Unexpected ')'";
                            return true;
                        }
                        tokens.Add(new FunkToken(pos, m.Length, TokKind.RParen, ")", false, false, depth - 1));
                        depth--;
                    }
                    endsWithBadToken = false;
                    pos += m.Length;
                    continue;
                }
                // 词法错误：无法识别的字符
                errStart = pos;
                errEnd = pos + 1;
                message = "Invalid character '" + text[pos] + "'";
                return true;
            }
            // ---- 第 2 层：token 流校验 ----
            TokKind prevKind = TokKind.None;
            bool prevIsName = false;
            int ternaryPending = 0;   // 未配对的 '?' 数（缺 ':' 的假分支）
            int lastTernaryPos = 0;   // 最近一个未配对 '?' 的位置（用于提示）
            // callParen[depth]：记录第 depth 层左括号是否为函数调用括号（前一个 token 是 name）。
            // 逗号只允许出现在函数调用的参数括号内，(a, b) 这类分组括号里不允许逗号。
            bool[] callParen = new bool[text.Length + 2];
            for (int i = 0; i < tokens.Count; i++)
            {
                FunkToken tk = tokens[i];
                switch (tk.kind)
                {
                    case TokKind.Value:
                        // 相邻值之间缺操作符（容错：name 后跟数据槽如 x[0] 不报）
                        if (prevKind == TokKind.Value && !(prevIsName && tk.isDataSlot))
                        {
                            errStart = tk.start; errEnd = tk.start + tk.length;
                            message = "Missing operator";
                            return true;
                        }
                        if (prevKind == TokKind.RParen)
                        {
                            errStart = tk.start; errEnd = tk.start + tk.length;
                            message = "Missing operator";
                            return true;
                        }
                        break;
                    case TokKind.Op:
                        // 三元 ?：前必须是表达式（条件），后必须是表达式（真分支），且需配对的 ':'
                        if (tk.value == "?")
                        {
                            if (prevKind == TokKind.Op || prevKind == TokKind.Comma
                                || prevKind == TokKind.None || prevKind == TokKind.LParen)
                            {
                                errStart = tk.start; errEnd = tk.start + tk.length;
                                message = "Missing expression before '?'";
                                return true;
                            }
                            if (!IsExpressionStart(tokens, i + 1))
                            {
                                errStart = tk.start; errEnd = tk.start + tk.length;
                                message = "Missing expression after '?'";
                                return true;
                            }
                            ternaryPending++;
                            lastTernaryPos = tk.start;
                            break;
                        }
                        // 三元 ':'：必须有配对的 '?'，且后必须是表达式（假分支）
                        if (tk.value == ":")
                        {
                            if (ternaryPending == 0)
                            {
                                errStart = tk.start; errEnd = tk.start + tk.length;
                                message = "Unexpected ':'";
                                return true;
                            }
                            ternaryPending--;
                            if (!IsExpressionStart(tokens, i + 1))
                            {
                                errStart = tk.start; errEnd = tk.start + tk.length;
                                message = "Missing expression after ':'";
                                return true;
                            }
                            break;
                        }
                        // unary - + !（负号/正号/逻辑非）允许在开头、操作符后、左括号后、逗号后
                        bool unary = tk.value == "-" || tk.value == "+" || tk.value == "!";
                        if ((prevKind == TokKind.Op || prevKind == TokKind.Comma || prevKind == TokKind.None) && !unary)
                        {
                            errStart = tk.start; errEnd = tk.start + tk.length;
                            message = "Unexpected operator '" + tk.value + "'";
                            return true;
                        }
                        if (prevKind == TokKind.LParen && !unary)
                        {
                            errStart = tk.start; errEnd = tk.start + tk.length;
                            message = "Unexpected operator '" + tk.value + "'";
                            return true;
                        }
                        break;
                    case TokKind.LParen:
                        // '(' 前只能是 name（函数调用）或操作符/左括号/开头；非 name 值后是错误
                        if (prevKind == TokKind.Value && !prevIsName)
                        {
                            errStart = tk.start; errEnd = tk.start + tk.length;
                            message = "Unexpected '('";
                            return true;
                        }
                        if (prevKind == TokKind.RParen)
                        {
                            errStart = tk.start; errEnd = tk.start + tk.length;
                            message = "Unexpected '('";
                            return true;
                        }
                        // 记录该层括号是否为函数调用括号：前一个 token 是 name 才允许参数逗号
                        callParen[tk.depth] = prevKind == TokKind.Value && prevIsName;
                        break;
                    case TokKind.RParen:
                        if (prevKind == TokKind.Op || prevKind == TokKind.Comma || prevKind == TokKind.LParen)
                        {
                            errStart = tk.start; errEnd = tk.start + tk.length;
                            message = "Missing expression";
                            return true;
                        }
                        break;
                    case TokKind.Comma:
                        // 逗号只能用在函数调用的参数括号内（分组括号 (a, b) 不允许逗号）
                        if (tk.depth == 0 || !callParen[tk.depth - 1])
                        {
                            errStart = tk.start; errEnd = tk.start + tk.length;
                            message = "Unexpected ','";
                            return true;
                        }
                        if (prevKind == TokKind.Op || prevKind == TokKind.LParen || prevKind == TokKind.Comma)
                        {
                            errStart = tk.start; errEnd = tk.start + tk.length;
                            message = "Missing expression";
                            return true;
                        }
                        break;
                }
                prevKind = tk.kind;
                prevIsName = tk.isName;
            }

            // 三元未闭合：'?' 缺对应的 ':'（缺假分支，游戏 SquashTernary 报 "Expecting : after ?"）
            if (ternaryPending > 0)
            {
                errStart = lastTernaryPos;
                errEnd = text.Length;
                message = "Expecting ':'";
                return true;
            }

            // ---- 变量 / 裸名引用校验 ----
            // 游戏 VariableToken.Create 的解析链：常量 -> Context 属性 -> VariableResolve 委托
            // （Controls 裸属性 / AGxx / FlightData.X / FD.X / OrbitData.X / OD.X / Part.Modifier.Property
            //   / v:向量变量 / 数字变量）。裸名不是函数/常量/Controls/AG/已定义变量时，CustomResolve
            //   （CraftService）会兜底成"数字变量"token，运行时 GetVariableValueAsDoubleAndThrow 抛
            //   "Could not find Vizzy variable..."。这里提前在编辑器里标出这一类运行时错误。
            // 拿不到飞行程序（variableNames == null）时跳过，避免误报。
            if (variableNames != null)
            {
                for (int i = 0; i < tokens.Count; i++)
                {
                    FunkToken tk = tokens[i];
                    if (tk.kind != TokKind.Value || !tk.isName)
                    {
                        continue;
                    }
                    string name = tk.value;

                    // v:name（向量变量）必须已定义（全局变量 + 程序树 IsDefinition 定义）。
                    if (name.StartsWith("v:", StringComparison.Ordinal))
                    {
                        string varName = name.Substring(2);
                        if (varName.Length == 0)
                        {
                            continue;
                        }
                        int dot = varName.IndexOf('.');
                        string baseName = dot >= 0 ? varName.Substring(0, dot) : varName;
                        if (!variableNames.Contains(baseName))
                        {
                            errStart = tk.start;
                            errEnd = tk.start + tk.length;
                            message = "Unknown variable '" + baseName + "'";
                            return true;
                        }
                        continue;
                    }

                    // 数据属性前缀（FlightData./FD./OrbitData./OD.）交给下面的属性校验层。
                    if (name.StartsWith("FlightData.", StringComparison.Ordinal)
                        || name.StartsWith("FD.", StringComparison.Ordinal)
                        || name.StartsWith("OrbitData.", StringComparison.Ordinal)
                        || name.StartsWith("OD.", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    // 带点裸名（Part.Modifier.Property 动态零件访问）不静态校验。
                    if (name.IndexOf('.') >= 0)
                    {
                        continue;
                    }

                    // 后跟 '(' 是函数调用：必须在内建函数里（含 list*、sum/rate/PID/smooth）。
                    bool isCall = i + 1 < tokens.Count && tokens[i + 1].kind == TokKind.LParen;
                    if (isCall)
                    {
                        if (Functions.Contains(name))
                        {
                            continue;
                        }
                        errStart = tk.start;
                        errEnd = tk.start + tk.length;
                        message = "Unknown function '" + name + "'";
                        return true;
                    }

                    // 裸名：常量 / Controls 属性 / AGxx 前缀 / 已定义变量 之一合法。
                    if (Keywords.Contains(name) || ControlsProperties.Contains(name)
                        || variableNames.Contains(name) || IsActionGroupName(name))
                    {
                        continue;
                    }
                    errStart = tk.start;
                    errEnd = tk.start + tk.length;
                    message = "Unknown variable '" + name + "'";
                    return true;
                }
            }

            // ---- FlightData / OrbitData 属性校验 ----
            // FlightData.X / FD.X / FlightData.Orbit.X / OrbitData.X 属性名必须存在。
            for (int i = 0; i < tokens.Count; i++)
            {
                FunkToken tk = tokens[i];
                if (tk.kind != TokKind.Value || !tk.isName)
                {
                    continue;
                }
                string err;
                if (!IsValidDataPropertyAccess(tk.value, out err))
                {
                    errStart = tk.start;
                    errEnd = tk.start + tk.length;
                    message = err;
                    return true;
                }
            }

            // ---- 第 3 层：函数参数个数校验 ----
            // 对已知函数（FunctionArgCounts 表内）的每次调用检查实参个数。
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].kind != TokKind.LParen || i == 0)
                {
                    continue;
                }
                FunkToken prev = tokens[i - 1];
                if (prev.kind != TokKind.Value || !prev.isName)
                {
                    continue; // 不是函数调用（如 2( 已在 token 流层报错）
                }
                int expected;
                if (!FunctionArgCounts.TryGetValue(prev.value.ToLowerInvariant(), out expected))
                {
                    continue; // 未知/自定义函数不校验
                }
                int lv = tokens[i].depth;
                int rp = -1;
                for (int j = i + 1; j < tokens.Count; j++)
                {
                    if (tokens[j].kind == TokKind.RParen && tokens[j].depth == lv)
                    {
                        rp = j;
                        break;
                    }
                }
                if (rp < 0)
                {
                    continue; // 未闭合（将由 Missing ')' 报）
                }
                int commas = 0;
                bool hasAny = false;
                for (int j = i + 1; j < rp; j++)
                {
                    if (tokens[j].kind == TokKind.Comma && tokens[j].depth == lv + 1)
                    {
                        commas++;
                    }
                    if (tokens[j].kind != TokKind.Comma)
                    {
                        hasAny = true;
                    }
                }
                int args = hasAny ? commas + 1 : 0;
                if (args != expected)
                {
                    errStart = prev.start;
                    errEnd = prev.start + prev.length;
                    message = "Function " + prev.value + " expects " + expected
                        + " argument" + (expected == 1 ? "" : "s") + ", " + args + " given";
                    return true;
                }
                // 列表检索函数：第一个参数是字符串列表名 -> 校验列表变量是否存在
                // （游戏 GetListValueAndThrow 运行时查变量，不存在/非列表会抛 "Could not find
                //   Vizzy variable..."。与裸名 bc 同类的漏检，这里提前标出。）
                if (variableNames != null
                    && (prev.value == "listNum" || prev.value == "listVec" || prev.value == "listBool"
                        || prev.value == "listStr" || prev.value == "listLen"))
                {
                    FunkToken firstArg = tokens[i + 1];
                    if (firstArg.kind == TokKind.Value && !firstArg.isName
                        && firstArg.value.Length >= 2 && firstArg.value[0] == '"'
                        && firstArg.value[firstArg.value.Length - 1] == '"')
                    {
                        string listName = firstArg.value.Substring(1, firstArg.value.Length - 2);
                        listName = listName.Replace("\\\"", "\"").Replace("\\\\", "\\");
                        if (listName.Length > 0 && !variableNames.Contains(listName))
                        {
                            errStart = firstArg.start;
                            errEnd = firstArg.start + firstArg.length;
                            message = "Unknown list '" + listName + "'";
                            return true;
                        }
                    }
                }
            }
            if (endsWithBadToken)
            {
                errStart = badTokenPos;
                errEnd = text.Length;
                message = "Incomplete expression";
                return true;
            }
            if (depth > 0)
            {
                // 未闭合的 '('：从最后一个未匹配的开括号到行尾标红
                errStart = FindOpenParenStart(tokens);
                errEnd = text.Length;
                message = "Missing ')'";
                return true;
            }
            return false;
        }

        /// <summary>
        /// 校验 FlightData / OrbitData 属性访问链：
        ///   FlightData.X / FD.X        -> X 必须在 FlightDataProperties
        ///   FlightData.Orbit.X         -> Orbit 在 FlightDataProperties，X 在 OrbitDataProperties
        /// 非数据属性前缀（普通变量、自定义等）返回 true 不校验。
        /// </summary>
        private static bool IsValidDataPropertyAccess(string name, out string error)
        {
            error = null;
            string path = null;
            bool orbitRoot = false;
            
             if (name.StartsWith("OrbitData.", StringComparison.Ordinal))
            {
                path = name.Substring("OrbitData.".Length);
                orbitRoot = true;
            }
            else if (name.StartsWith("FD.", StringComparison.Ordinal))
            {
                path = name.Substring("FD.".Length);
            }
            else if (name.StartsWith("FlightData.", StringComparison.Ordinal))
            {
                path = name.Substring("FlightData.".Length);
            }
            else
            {
                return true; // 非数据属性前缀，不校验
            }
            if (path.Length == 0)
            {
                return true; // "FlightData." / "OD." 单独出现
            }
            string[] segs = path.Split('.');
            if (orbitRoot)
            {
                foreach (string seg in segs)
                {
                    if (!OrbitDataProperties.Contains(seg))
                    {
                        string s = FindClosestProperty(seg, OrbitDataProperties);
                        error = "Unknown OrbitData property '" + seg + "'"
                            + (s != null ? " (did you mean '" + s + "'?)" : "");
                        return false;
                    }
                }
                return true;
            }
            // FlightData 链：第一段在 FlightDataProperties；若第一段是 Orbit，后续段查 OrbitDataProperties
            if (!FlightDataProperties.Contains(segs[0]))
            {
                string s = FindClosestProperty(segs[0], FlightDataProperties);
                error = "Unknown FlightData property '" + segs[0] + "'"
                    + (s != null ? " (did you mean '" + s + "'?)" : "");
                return false;
            }
            if (segs.Length > 1 && segs[0] == "Orbit")
            {
                for (int k = 1; k < segs.Length; k++)
                {
                    if (!OrbitDataProperties.Contains(segs[k]))
                    {
                        string s = FindClosestProperty(segs[k], OrbitDataProperties);
                        error = "Unknown OrbitData property '" + segs[k] + "'"
                            + (s != null ? " (did you mean '" + s + "'?)" : "");
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>在属性集合里找编辑距离 ≤ 2 的最近候选（用于拼写建议），找不到返回 null。</summary>
        private static string FindClosestProperty(string prop, HashSet<string> set)
        {
            string best = null;
            int bestDist = int.MaxValue;
            foreach (string candidate in set)
            {
                int dist = LevenshteinDistance(prop, candidate);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = candidate;
                }
            }
            return bestDist <= 2 ? best : null;
        }

        /// <summary>Levenshtein 编辑距离。</summary>
        private static int LevenshteinDistance(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++)
            {
                d[i, 0] = i;
            }
            for (int j = 0; j <= b.Length; j++)
            {
                d[0, j] = j;
            }
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[a.Length, b.Length];
        }

        private static int FindOpenParenStart(List<FunkToken> tokens)
        {
            for (int i = tokens.Count - 1; i >= 0; i--)
            {
                if (tokens[i].kind == TokKind.LParen)
                {
                    return tokens[i].start;
                }
            }
            return 0;
        }

        private enum TokKind { None, Value, Op, LParen, RParen, Comma }

        private struct FunkToken
        {
            public int start;
            public int length;
            public TokKind kind;
            public string value;
            public bool isName;
            public bool isDataSlot;
            public int depth;

            public FunkToken(int start, int length, TokKind kind, string value, bool isName, bool isDataSlot, int depth)
            {
                this.start = start;
                this.length = length;
                this.kind = kind;
                this.value = value;
                this.isName = isName;
                this.isDataSlot = isDataSlot;
                this.depth = depth;
            }
        }

        private static bool TryToken(string text, ref int pos, Color32[] colors, Regex pattern, Color32 color)
        {
            Match m = pattern.Match(text, pos);
            if (!m.Success || m.Index != pos)
            {
                return false;
            }
            Fill(colors, pos, m.Length, color);
            pos += m.Length;
            return true;
        }

        private static void Fill(Color32[] colors, int start, int length, Color32 color)
        {
            for (int i = start; i < start + length; i++)
            {
                colors[i] = color;
            }
        }

        private static Color32 KindOfName(string name)
        {
            if (name.StartsWith("v:", StringComparison.Ordinal))
            {
                return ColorVector;
            }
            if (name.IndexOf('.') >= 0 || name.IndexOf('$') >= 0)
            {
                return ColorPart;
            }
            if (Functions.Contains(name))
            {
                return ColorFunction;
            }
            if (Keywords.Contains(name) || (name.Length > 2 && name[0] == 'A' && name[1] == 'G' && IsDigits(name.Substring(2))))
            {
                return ColorKeyword;
            }
            return ColorVariable;
        }

        private static bool IsDigits(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            foreach (char c in s)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }
            return true;
        }

        private static Color32 FromHex(string hex)
        {
            byte r = Convert.ToByte(hex.Substring(1, 2), 16);
            byte g = Convert.ToByte(hex.Substring(3, 2), 16);
            byte b = Convert.ToByte(hex.Substring(5, 2), 16);
            return new Color32(r, g, b, 255);
        }

        private static void LogOnce(string msg)
        {
            if (_loggedOnce.Add(msg))
            {
                Debug.Log(msg);
            }
        }
    }

    /// <summary>块启动时注册 textComponent -> 输入框 映射并订阅 OnPreRenderText。</summary>
    [HarmonyPatch(typeof(TextInputElementScript), "Start")]
    public static class FunkHighlightRegisterPatch
    {
        private static void Postfix(TextInputElementScript __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }
                TMP_InputField input = __instance.GetComponent<TMP_InputField>();
                if (input != null)
                {
                    FunkHighlight.Register(input);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FunkHighlight] Register Start: " + e.Message);
            }
        }
    }
}
