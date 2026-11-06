namespace Assets.Scripts
{
    using ModApi.Settings.Core;
    using UnityEngine;

    /// <summary>The letter keys the editor shortcut can use, with the chosen modifier.</summary>
    public enum EditorKey
    {
        A,

        B,

        C,

        D,

        E,

        F,

        G,

        H,

        I,

        J,

        K,

        L,

        M,

        N,

        O,

        P,

        Q,

        R,

        S,

        T,

        U,

        V,

        W,

        X,

        Y,

        Z,
    }

    /// <summary>
    /// The key held with the editor letter. Shift and Ctrl are the throttle in the game's flight
    /// controls, so Alt (Option on a Mac) is the default.
    /// </summary>
    public enum EditorModifier
    {
        [EnumOption("Option / Alt", "Option on a Mac.")]
        Alt,

        [EnumOption("Ctrl", "Also lowers the throttle.")]
        Control,

        [EnumOption("Shift", "Also raises the throttle.")]
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

        public BoolSetting CheckForUpdates { get; private set; }

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
            this.CheckForUpdates = this.CreateBool("Check For Updates", "CheckForUpdates")
                .SetDescription("At game start, reads the latest version from the mod's SimpleRockets.com page and, if it's newer, offers to open the page. Nothing is downloaded automatically.")
                .SetDefault(true);

            this.DebugLogging = this.CreateBool("Debug Logging", "DebugLogging")
                .SetDescription("Writes thread positions and other details to Player.log.")
                .SetDefault(false);

            this.Key = this.CreateEnum<EditorKey>("Editor Key", "Key")
                .SetDescription("With the modifier below, opens the Vizzy editor in flight. Pressed again, it applies the edit.")
                .SetDefault(EditorKey.A);

            this.Modifier = this.CreateEnum<EditorModifier>("Editor Key Modifier", "Modifier")
                .SetDescription("Held with the editor key.")
                .SetDefault(EditorModifier.Alt);

            this.ShowFlightButtons = this.CreateBool("Show Flight Buttons", "ShowFlightButtons")
                .SetDescription("Shows a Vizzy button on the flight nav panel, with Restart Vizzy and Edit Vizzy. Applies from the next flight.")
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
