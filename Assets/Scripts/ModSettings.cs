namespace Assets.Scripts
{
    using ModApi.Settings.Core;
    using UnityEngine;

    /// <summary>The letter keys the editor shortcut can use.</summary>
    public enum EditorKey
    {
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    }

    /// <summary>The mod's settings (Settings → Mods → Vizzy Flight Editor).</summary>
    public class ModSettings : SettingsCategory<ModSettings>
    {
        private static ModSettings _instance;

        public ModSettings() : base("Vizzy Flight Editor")
        {
        }

        public static ModSettings Instance => _instance ?? (_instance = Game.Instance.Settings.ModSettings.GetCategory<ModSettings>());

        public EnumSetting<EditorKey> Key { get; private set; }

        public BoolSetting RequireShift { get; private set; }

        public BoolSetting DebugLogging { get; private set; }

        public BoolSetting TestAutoFillKeys { get; private set; }

        public KeyCode KeyCode => KeyCode.A + (int)this.Key.Value;

        /// <summary>Gets the shortcut as text, for messages.</summary>
        public string ShortcutText => (this.RequireShift.Value ? "Shift+" : string.Empty) + this.Key.Value;

        // XML names are given explicitly: the game derives them from the display name otherwise.
        protected override void InitializeSettings()
        {
            this.Key = this.CreateEnum<EditorKey>("Editor Key", "Key")
                .SetDescription("Opens the Vizzy editor in flight. In the editor it applies the edit and returns to flight. With Alt it returns to the quicksave's program.")
                .SetDefault(EditorKey.O);

            this.RequireShift = this.CreateBool("Editor Key Needs Shift", "RequireShift")
                .SetDescription("The editor key only works together with Shift.")
                .SetDefault(true);

            this.DebugLogging = this.CreateBool("Debug Logging", "DebugLogging")
                .SetDescription("Writes thread positions and other details to Player.log.")
                .SetDefault(false);

            this.TestAutoFillKeys = this.CreateBool("Test Auto-Fill Keys", "TestAutoFillKeys")
                .SetDescription("Temporary check of key handling in a Funk field: Up and Down keep the cursor in place, Enter inserts TEST.")
                .SetDefault(true);
        }
    }
}
