using Assets.Scripts.Craft.Parts.Modifiers;
using Assets.Scripts.Craft.Parts.Modifiers.Mfd;
using Assets.Scripts.Vizzy;
using Assets.Scripts.Vizzy.UI;
using HarmonyLib;
using ModApi;
using ModApi.Craft.Program;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts
{
    using System;
using System.Linq;
using System.Xml.Linq;
using Assets.Packages.DevConsole;
using Assets.Scripts.Flight;
using Assets.Scripts.Menu.ListView;
using ModApi.Craft;
using ModApi.Mods;
using ModApi.State;
using ModApi.Ui;
using UI.Xml;
using static ModApi.Craft.Parts.PartData;

    /// <summary>
    /// A singleton object representing this mod that is instantiated and initialize when the mod is loaded.
    /// </summary>
    public partial class Mod : ModApi.Mods.GameMod
    {
        /// <summary>
        /// Prevents a default instance of the <see cref="Mod"/> class from being created.
        /// </summary>
        private Mod() : base()
        {
        }

        /// <summary>
        /// Gets the singleton instance of the mod object.
        /// </summary>
        /// <value>The singleton instance of the mod object.</value>
        public static Mod Instance { get; } = GetModInstance<Mod>();
        public override void OnModLoaded()
        {
            base.OnModLoaded();
            Harmony harmony = new Harmony("com.SatelliteTorifune.VizzyDebugger");
            harmony.PatchAll();
            // Funk 语法高亮：不在 OnModLoaded 里注册方法 patch——
            // 着色通过订阅 TMP_Text.OnPreRenderText 事件实现（见 FunkHighlight.Register，
            // 由 [HarmonyPatch(TextInputElementScript, Start)] 在块创建时调用）。
            // OnPreRenderText 在 TMP 拷 mesh 数据（m_mesh.colors32）之前触发，
            // 此时改 meshInfo.colors32 能被随后上传，颜色一定生效。
            //RegisterModVizzyNode();
            Game.Instance.UserInterface.AddBuildUserInterfaceXmlAction("Ui/Xml/Flight/ViewPanel", OnBuildViewPanel); 
            Game.Instance.SceneManager.SceneLoaded += (sender, e) => 
            {
                if (Game.Instance.SceneManager.InFlightScene)
                {
                    Game.Instance.FlightScene.GameObject.GetComponentsInChildren<XmlElement>().ToList().ForEach(x =>
                    {
                        if (x.id == "edit-vizzy")
                        {
                            x.AddOnClickEvent(EditVizzy);
                        }
                        if (x.id == "restart-vizzy")
                        {
                            x.AddOnClickEvent(RestartFlightProgram);
                        }
                    });
                }
            };


            DevConsoleApi.RegisterCommand("CleanUnsedVar",VizzyVariableCleaner.CleanCurrentEditingProgram);

            
        }
        
        public void EditVizzy()
        {
            var partData=ModApi.Common.Game.Instance.FlightScene.ViewManager.GameView.SelectedPart.Data;
            if (partData==null)
            {
                Game.Instance.FlightScene.FlightSceneUI.ShowMessage(Locale.GetString("Design.Symmetry.MessageNoPartSelected"));
                return;
            }
            FlightProgramData flightProgram = partData.GetModifier<FlightProgramData>();
            if (flightProgram == null)
            {
                flightProgram = FlightProgramData.Create(partData);
                flightProgram.PowerConsumptionPerInstruction = 0.01f;
                flightProgram.BroadcastPowerConsumptionPerByte = 0.1f;
            }

            if (flightProgram != null)
            {
                XElement flightProgramXml = flightProgram.FlightProgramXml;
                VizzyUIScript vizzyUIScript = Game.Instance.ResourceLoader.InstantiatePrefab<VizzyUIScript>("Ui/Vizzy/Vizzy");
                FlightSceneScript.Instance.TimeManager.RequestPauseChange(true, false);
                //FlightSceneScript.Instance.InputEnabled = false;
                vizzyUIScript.Initialize(Game.Instance.UserInterface.Transform, flightProgramXml, partData.GetModifier<MfdData>() != null, delegate (XElement xml)
                {
                    flightProgram.FlightProgramXml = xml;
                });
                vizzyUIScript.Closed += delegate
                {
                    FlightSceneScript.Instance.TimeManager.RequestPauseChange(false, false);
                    //Designer.InputEnabled = true;
                };
            }
            
        }
        private void RestartFlightProgram()
        {
            var partData=ModApi.Common.Game.Instance.FlightScene.ViewManager.GameView.SelectedPart.Data;
            if (partData==null)
            {
                Game.Instance.FlightScene.FlightSceneUI.ShowMessage(Locale.GetString("Design.Symmetry.MessageNoPartSelected"));
                return;
            }
            FlightProgramData program = partData.GetModifier<FlightProgramData>();
            if (program != null)
            {
                program.ProcessXml = null;
                program.Script.StartProgram();
            }
        }
    }
}