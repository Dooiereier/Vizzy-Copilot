namespace Assets.Scripts
{
    using ModApi.Scenes.Events;
    using UnityEngine;

    public class Mod : ModApi.Mods.GameMod
    {
        private Mod() : base()
        {
        }

        public static Mod Instance { get; } = GetModInstance<Mod>();

        // Programs stay ordinary stock XML, so crafts and saves never need this mod.
        public override bool IsModRequiredForCraft(ModApi.Craft.CraftData craftData) => false;

        protected override void OnModInitialized()
        {
            Game.Instance.SceneManager.SceneLoaded += OnSceneLoaded;
            Debug.Log("[VizzyFlightEditor] Started.");
        }

        private void OnSceneLoaded(object sender, SceneEventArgs e)
        {
            if (Game.InFlightScene)
            {
                new GameObject("VizzyFlightEditor.Controller").AddComponent<FlightEditorController>();
            }
            else
            {
                LatestEdits.Clear();
            }
        }
    }
}
