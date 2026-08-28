using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class CaptureFailurePolicyTests
    {
        [Fact]
        public void FirstFailure_Notifies_ThenSuppresses_UntilPause()
        {
            var policy = new CaptureFailurePolicy(maxConsecutive: 5);

            Assert.Equal(CaptureFailureAction.Notify, policy.RecordFailure());
            Assert.Equal(CaptureFailureAction.Suppress, policy.RecordFailure());
            Assert.Equal(CaptureFailureAction.Suppress, policy.RecordFailure());
            Assert.Equal(CaptureFailureAction.Suppress, policy.RecordFailure());
            Assert.Equal(CaptureFailureAction.Pause, policy.RecordFailure());
        }

        [Fact]
        public void Success_ClearsTheStreak_AndReportsHowManyWereCleared()
        {
            var policy = new CaptureFailurePolicy(maxConsecutive: 5);
            policy.RecordFailure();
            policy.RecordFailure();

            Assert.Equal(2, policy.RecordSuccess());
            Assert.Equal(0, policy.RecordSuccess());   // nothing pending now

            // Streak restarts from the beginning after recovery.
            Assert.Equal(CaptureFailureAction.Notify, policy.RecordFailure());
        }

        [Fact]
        public void SuccessBetweenFailures_PreventsAutoPause()
        {
            var policy = new CaptureFailurePolicy(maxConsecutive: 3);

            policy.RecordFailure();   // Notify
            policy.RecordFailure();   // Suppress
            policy.RecordSuccess();   // recovered before the 3rd

            Assert.Equal(CaptureFailureAction.Notify, policy.RecordFailure());
            Assert.Equal(CaptureFailureAction.Suppress, policy.RecordFailure());
            Assert.Equal(CaptureFailureAction.Pause, policy.RecordFailure());
        }

        [Fact]
        public void StaysPaused_OnFurtherFailuresAfterTheThreshold()
        {
            var policy = new CaptureFailurePolicy(maxConsecutive: 2);

            Assert.Equal(CaptureFailureAction.Notify, policy.RecordFailure());
            Assert.Equal(CaptureFailureAction.Pause, policy.RecordFailure());
            Assert.Equal(CaptureFailureAction.Pause, policy.RecordFailure());   // still paused
            Assert.Equal(CaptureFailureAction.Pause, policy.RecordFailure());
        }
    }
}
