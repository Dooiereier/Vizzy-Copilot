namespace Assets.Scripts
{
    using ModApi.Settings.Core;
    using UnityEngine;

    /// <summary>The letter keys the editor shortcut can use, always with Shift.</summary>
    public enum EditorKey
    {
        [EnumOption("Shift - A")]
        A,

        [EnumOption("Shift - B")]
        B,

        [EnumOption("Shift - C")]
        C,

        [EnumOption("Shift - D")]
        D,

        [EnumOption("Shift - E")]
        E,

        [EnumOption("Shift - F")]
        F,

        [EnumOption("Shift - G")]
        G,

        [EnumOption("Shift - H")]
        H,

        [EnumOption("Shift - I")]
        I,

        [EnumOption("Shift - J")]
        J,

        [EnumOption("Shift - K")]
        K,

        [EnumOption("Shift - L")]
        L,

        [EnumOption("Shift - M")]
        M,

        [EnumOption("Shift - N")]
        N,

        [EnumOption("Shift - O")]
        O,

        [EnumOption("Shift - P")]
        P,

        [EnumOption("Shift - Q")]
        Q,

        [EnumOption("Shift - R")]
        R,

        [EnumOption("Shift - S")]
        S,

        [EnumOption("Shift - T")]
        T,

        [EnumOption("Shift - U")]
        U,

        [EnumOption("Shift - V")]
        V,

        [EnumOption("Shift - W")]
        W,

        [EnumOption("Shift - X")]
        X,

        [EnumOption("Shift - Y")]
        Y,

        [EnumOption("Shift - Z")]
        Z,
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

        public BoolSetting DebugLogging { get; private set; }

        public BoolSetting ShowFlightButtons { get; private set; }

        public BoolSetting ExportMenu { get; private set; }

        public BoolSetting CheckForUpdates { get; private set; }

        public BoolSetting BlockSearch { get; private set; }

        public BoolSetting FunkAutoFill { get; private set; }

        public BoolSetting FunkSyntaxHighlight { get; private set; }

        public KeyCode KeyCode => KeyCode.A + (int)this.Key.Value;

        /// <summary>Gets the shortcut as text, for messages.</summary>
        public string ShortcutText => "Shift+" + (char)('A' + (int)this.Key.Value);

        // XML names are given explicitly: the game derives them from the display name otherwise.
        protected override void InitializeSettings()
        {
            this.Key = this.CreateEnum<EditorKey>("Editor Key", "Key")
                .SetDescription("Opens the Vizzy editor in flight. In the editor it applies the edit and returns to flight. With Alt it returns to the quicksave's program.")
                .SetDefault(EditorKey.A);

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
                .SetDescription("Shift+F in the Vizzy editor opens a search box: type part of a block's name, then drag it out of the results.")
                .SetDefault(true);

            this.FunkSyntaxHighlight = this.CreateBool("Funk Syntax Highlight", "FunkSyntaxHighlight")
                .SetDescription("Colors Funk fields and marks errors in red, in the designer and in flight.")
                .SetDefault(true);

            this.FunkAutoFill = this.CreateBool("Funk Auto-Fill", "FunkAutoFill")
                .SetDescription("Suggests Funk names while typing in a Funk field, in the designer and in flight. Up and Down choose, Enter takes it, Esc closes the list.")
                .SetDefault(true);
        }
    }
}
