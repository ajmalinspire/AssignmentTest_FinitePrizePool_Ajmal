using FinitePrizePool.Engine.Persistence;

namespace FinitePrizePool.Game
{
    /// <summary>
    /// The result display's state, kept apart from drawing. A spin's outcome is already saved when it starts;
    /// this only decides when the player sees it. The result counts as shown once it is revealed, so closing
    /// the game while it is still spinning brings the same result back on the next launch.
    /// </summary>
    public sealed class SpinPresenter
    {
        public enum Phase
        {
            Idle,
            Spinning,
            Revealed
        }

        private readonly float _spinSeconds;
        private float _remaining;

        public Phase State { get; private set; } = Phase.Idle;
        public SpinRecord Current { get; private set; }

        /// <summary>True for a result restored from the last session (the game closed before it was shown).</summary>
        public bool Restored { get; private set; }

        public bool IsSpinning => State == Phase.Spinning;
        public float Progress => IsSpinning && _spinSeconds > 0 ? 1f - _remaining / _spinSeconds : 1f;

        public SpinPresenter(float spinSeconds)
        {
            _spinSeconds = spinSeconds;
        }

        public void Begin(SpinRecord record, bool restored)
        {
            Current = record;
            Restored = restored;
            State = Phase.Spinning;
            _remaining = restored ? 0f : _spinSeconds; // a restored result is shown straight away
        }

        /// <summary>Advances the animation. Returns true once, on the frame the result is revealed.</summary>
        public bool Tick(float deltaSeconds)
        {
            if (State != Phase.Spinning) return false;
            _remaining -= deltaSeconds;
            if (_remaining > 0f) return false;
            State = Phase.Revealed;
            return true;
        }
    }
}
