namespace CaptionScribe.Services
{
    /// <summary>What the capture loop should do about the failure it just saw.</summary>
    internal enum CaptureFailureAction
    {
        /// <summary>First failure of a streak — tell the user once.</summary>
        Notify,

        /// <summary>Still failing — log it, but don't nag the user again.</summary>
        Suppress,

        /// <summary>Too many consecutive failures — stop trying and pause capture.</summary>
        Pause,
    }

    /// <summary>
    /// Tracks a run of consecutive capture failures so the loop can notify once at onset, stay quiet
    /// while it keeps failing, and auto-pause once a threshold is crossed.
    /// </summary>
    internal sealed class CaptureFailurePolicy
    {
        private readonly int _maxConsecutive;
        private int _failures;

        public CaptureFailurePolicy(int maxConsecutive) => _maxConsecutive = maxConsecutive;

        /// <summary>Clears the streak and returns how many failures were pending (0 if none).</summary>
        public int RecordSuccess()
        {
            int cleared = _failures;
            _failures = 0;
            return cleared;
        }

        public CaptureFailureAction RecordFailure()
        {
            _failures++;
            if (_failures >= _maxConsecutive)
                return CaptureFailureAction.Pause;
            return _failures == 1 ? CaptureFailureAction.Notify : CaptureFailureAction.Suppress;
        }
    }
}
