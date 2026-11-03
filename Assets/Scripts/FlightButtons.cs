namespace Assets.Scripts
{
    using System;
    using System.Linq;
    using System.Xml.Linq;
    using ModApi.Ui;
    using UI.Xml;

    /// <summary>Edit Vizzy and Restart Vizzy buttons on the flight view panel.</summary>
    public static class FlightButtons
    {
        private const string ViewPanelId = "Ui/Xml/Flight/ViewPanel";
        private const string EditId = "vizzy-studio-edit";
        private const string RestartId = "vizzy-studio-restart";

        public static void Register()
        {
            Game.Instance.UserInterface.AddBuildUserInterfaceXmlAction(ViewPanelId, OnBuildViewPanel);
        }

        /// <summary>The buttons sit in a child layout, so they're found once the flight scene is up.</summary>
        public static void Connect()
        {
            foreach (var element in Game.Instance.FlightScene.GameObject.GetComponentsInChildren<XmlElement>())
            {
                if (element.id == EditId)
                {
                    element.AddOnClickEvent(() => FlightEditorController.Current?.EditSelected());
                }
                else if (element.id == RestartId)
                {
                    element.AddOnClickEvent(() => FlightEditorController.Current?.AskToRestartSelected());
                }
            }
        }

        private static void OnBuildViewPanel(BuildUserInterfaceXmlRequest request)
        {
            try
            {
                if (!ModSettings.Instance.ShowFlightButtons.Value)
                {
                    return;
                }

                XNamespace ns = XmlLayoutConstants.XmlNamespace;
                var after = request.XmlDocument.Descendants(ns + "ContentButton")
                    .FirstOrDefault(button => (string)button.Attribute("id") == "toggle-camera-panel-button");
                if (after == null)
                {
                    EditorLog.Warning("Couldn't find where to put the flight buttons.");
                    return;
                }

                after.AddAfterSelf(Button(ns, RestartId, "Restart Vizzy", "VizzyStudio/Sprites/IconResetStaging"));
                after.AddAfterSelf(Button(ns, EditId, "Edit Vizzy", "VizzyStudio/Sprites/IconVariables"));
            }
            catch (Exception e)
            {
                EditorLog.Error("Couldn't add the flight buttons: " + e);
            }
        }

        private static XElement Button(XNamespace ns, string id, string tooltip, string sprite)
        {
            return new XElement(ns + "ContentButton",
                new XAttribute("name", id),
                new XAttribute("id", id),
                new XAttribute("class", "view-button audio-btn-click"),
                new XAttribute("tooltip", tooltip),
                new XElement(ns + "Image", new XAttribute("sprite", sprite)));
        }
    }
}
