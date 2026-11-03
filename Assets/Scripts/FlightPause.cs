namespace Assets.Scripts
{
    /// <summary>
    /// The pause while choosing or editing a program. Keeps the state from before the first hold: a
    /// pause request only takes effect at the end of the frame, and later reads would see this pause.
    /// </summary>
    public static class FlightPause
    {
        private static bool _held;
        private static bool _wasPaused;

        public static void Hold()
        {
            if (!_held)
            {
                var time = Game.Instance.FlightScene.TimeManager;
                _held = true;
                _wasPaused = time.Paused;
                time.RequestPauseChange(true, false);
            }
        }

        public static void Release()
        {
            if (_held)
            {
                _held = false;
                if (!_wasPaused)
                {
                    Game.Instance.FlightScene.TimeManager.RequestPauseChange(false, false);
                }
            }
        }

        /// <summary>A new flight scene starts with no hold.</summary>
        public static void Reset()
        {
            _held = false;
        }
    }
}
