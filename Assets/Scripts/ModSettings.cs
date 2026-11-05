namespace Assets.Scripts
{
    using ModApi.Settings.Core;
    using UnityEngine;

    /// <summary>The letter keys the editor shortcut can use, with the chosen modifier.</summary>
    public enum EditorKey
    {
        [EnumOption("A")]
        A,

        [EnumOption("B")]
        B,

        [EnumOption("C")]
        C,

        [EnumOption("D")]
        D,

        [EnumOption("E")]
        E,

        [EnumOption("F")]
        F,

        [EnumOption("G")]
        G,

        [EnumOption("H")]
        H,

        [EnumOption("I")]
        I,

        [EnumOption("J")]
        J,

        [EnumOption("K")]
        K,

        [EnumOption("L")]
        L,

        [EnumOption("M")]
        M,

        [EnumOption("N")]
        N,

        [EnumOption("O")]
        O,

        [EnumOption("P")]
        P,

        [EnumOption("Q")]
        Q,

        [EnumOption("R")]
        R,

        [EnumOption("S")]
        S,

        [EnumOption("T")]
        T,

        [EnumOption("U")]
        U,

        [EnumOption("V")]
        V,

        [EnumOption("W")]
        W,

        [EnumOption("X")]
        X,

        [EnumOption("Y")]
        Y,

        [EnumOption("Z")]
        Z,
    }

    /// <summary>
    /// The key held with the editor letter. Shift and Ctrl are the throttle in the game's flight
    /// controls, so Alt (Option on a Mac) is the default.
    /// </summary>
    public enum EditorModifier
    {
        [EnumOption("Option / Alt")]
        Alt,

        [EnumOption("Ctrl")]
        Control,

        [EnumOption("Shift")]
        Shift,
    }

    /// <summary>The mod's settings (Settings → Mods → Vizzy Studio).</summary>
    public class ModSettings : SettingsCategory<ModSettings>
    {
        private static ModSettings _instance;

        public ModSettings() : base("Vizzy Studio")
        {
        }

        public static ModSettings Instance => _instance ?? (_instance = Game.Instance.Settings.ModSettings.GetCategory<ModSettings>());

        public EnumSetting<EditorKey> Key { get; private set; }

        public EnumSetting<EditorModifier> Modifier { get; private set; }

        public BoolSetting DebugLogging { get; private set; }

        public BoolSetting ShowFlightButtons { get; private set; }

        public BoolSetting ExportMenu { get; private set; }

        public BoolSetting CheckForUpdates { get; private set; }

        public BoolSetting BlockSearch { get; private set; }

        public BoolSetting ArrangeMfd { get; private set; }

        public BoolSetting FunkAutoFill { get; private set; }

        public BoolSetting FunkSyntaxHighlight { get; private set; }

        public KeyCode KeyCode => KeyCode.A + (int)this.Key.Value;

        /// <summary>Gets the shortcut as text, for messages.</summary>
        public string ShortcutText => ModifierName(this.Modifier.Value) + "+" + (char)('A' + (int)this.Key.Value);

        /// <summary>Gets the shortcut that goes back to the quicksave's program, as text.</summary>
        public string RevertShortcutText => ModifierName(this.Modifier.Value) + "+" + ModifierName(RevertModifier(this.Modifier.Value)) + "+" + (char)('A' + (int)this.Key.Value);

        /// <summary>The extra key that makes the shortcut a revert: Shift, or Alt when Shift is the modifier.</summary>
        public static EditorModifier RevertModifier(EditorModifier modifier)
        {
            return modifier == EditorModifier.Shift ? EditorModifier.Alt : EditorModifier.Shift;
        }

        public static bool IsHeld(EditorModifier modifier)
        {
            switch (modifier)
            {
                case EditorModifier.Alt:
                    return UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);
                case EditorModifier.Control:
                    return UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
                default:
                    return UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            }
        }

        public static string ModifierName(EditorModifier modifier)
        {
            switch (modifier)
            {
                case EditorModifier.Alt:
                    return Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor ? "Option" : "Alt";
                case EditorModifier.Control:
                    return "Ctrl";
                default:
                    return "Shift";
            }
        }

        // XML names are given explicitly: the game derives them from the display name otherwise.
        protected override void InitializeSettings()
        {
            this.Key = this.CreateEnum<EditorKey>("Editor Key", "Key")
                .SetDescription("With the Editor Key Modifier, opens the Vizzy editor in flight. In the editor it applies the edit and returns to flight. Adding Shift, or Option / Alt when Shift is the modifier, returns to the quicksave's program.")
                .SetDefault(EditorKey.A);

            this.Modifier = this.CreateEnum<EditorModifier>("Editor Key Modifier", "Modifier")
                .SetDescription("The key held with the Editor Key. In the game's flight controls Shift and Ctrl change the throttle, so pressing them also moves it.")
                .SetDefault(EditorModifier.Alt);

            this.DebugLogging = this.CreateBool("Debug Logging", "DebugLogging")
                .SetDescription("Writes thread positions and other details to Player.log.")
                .SetDefault(false);

            this.ShowFlightButtons = this.CreateBool("Show Flight Buttons", "ShowFlightButtons")
                .SetDescription("Shows a Vizzy button on the flight nav panel, with Restart Vizzy and Edit Vizzy. Applies from the next flight.")
                .SetDefault(true);

            this.ExportMenu = this.CreateBool("Export Block", "ExportMenu")
                .SetDescription("Adds an Export Block button to the Vizzy menu, which copies the clicked block, with the variables and custom blocks it needs, to another saved program.")
                .SetDefault(true);

            this.CheckForUpdates = this.CreateBool("Check For Updates", "CheckForUpdates")
                .SetDescription("At game start, reads the latest version from the mod's SimpleRockets.com page and, if it's newer, offers to open the page. Nothing is downloaded automatically.")
                .SetDefault(true);

            this.BlockSearch = this.CreateBool("Block Search", "BlockSearch")
                .SetDescription("Shift+F or a search button in the Vizzy editor opens a search box: type part of a block's name, then drag it out of the results.")
                .SetDefault(true);

            this.ArrangeMfd = this.CreateBool("Arrange MFD", "ArrangeMfd")
                .SetDescription("Adds Arrange MFD to the flight buttons: move, resize and rotate the widgets of a custom MFD program, then save the values into the program. Saving also changes the craft's files, after backing them up. Applies from the next flight.")
                .SetDefault(false);

            this.FunkSyntaxHighlight = this.CreateBool("Funk Syntax Highlight", "FunkSyntaxHighlight")
                .SetDescription("Colors Funk fields and marks errors in red, in the designer and in flight.")
                .SetDefault(true);

            this.FunkAutoFill = this.CreateBool("Funk Auto-Fill", "FunkAutoFill")
                .SetDescription("Suggests Funk names while typing in a Funk field, in the designer and in flight. Up and Down choose, Enter takes it, Esc closes the list.")
                .SetDefault(true);
        }
    }
}
