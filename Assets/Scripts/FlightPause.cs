namespace Assets.Scripts
{
    using System.Collections.Generic;
    using ModApi.Input;
    using UnityEngine;

    /// <summary>
    /// Holds the flight still while choosing or editing a program: pauses it and turns off the game's
    /// keys, which otherwise still fire while paused (stage, activation groups, pause, warp, quickload).
    /// Keeps the state from before the first hold: a pause request only takes effect at the end of the
    /// frame, and later reads would see this pause.
    /// </summary>
    public static class FlightPause
    {
        private static readonly List<IGameInput> DisabledInputs = new List<IGameInput>();
        private static bool _held;
        private static bool _wasPaused;
        private static int _holdFrame;

        public static void Hold()
        {
            if (_held)
            {
                return;
            }

            var time = Game.Instance.FlightScene.TimeManager;
            _held = true;
            _wasPaused = time.Paused;
            _holdFrame = Time.frameCount;
            time.RequestPauseChange(true, false);

            var inputs = Game.Instance.Inputs;
            foreach (var input in inputs.AllInputs)
            {
                // The editor reads these two.
                if (input.Enabled && input != inputs.Undo && input != inputs.Redo)
                {
                    input.Enabled = false;
                    DisabledInputs.Add(input);
                }
            }
        }

        /// <summary>Each frame while held: raw keys such as Esc can still reach the menu, which un-pauses on closing.</summary>
        public static void Enforce()
        {
            if (_held && Time.frameCount > _holdFrame + 1)
            {
                var time = Game.Instance.FlightScene.TimeManager;
                if (!time.Paused)
                {
                    time.RequestPauseChange(true, false);
                }
            }
        }

        public static void Release()
        {
            if (!_held)
            {
                return;
            }

            _held = false;
            RestoreInputs();
            if (!_wasPaused)
            {
                Game.Instance.FlightScene.TimeManager.RequestPauseChange(false, false);
            }
        }

        /// <summary>A flight scene ends: inputs outlive it, and the next scene sets its own time mode.</summary>
        public static void Reset()
        {
            _held = false;
            RestoreInputs();
        }

        private static void RestoreInputs()
        {
            foreach (var input in DisabledInputs)
            {
                input.Enabled = true;
            }

            DisabledInputs.Clear();
        }
    }
}
