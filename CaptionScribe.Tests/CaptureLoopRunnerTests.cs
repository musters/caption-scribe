using System;
using System.Threading;
using System.Threading.Tasks;
using CaptionScribe.Core.Logging;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class CaptureLoopRunnerTests
    {
        [Fact]
        public async Task RunAsync_WhenAlreadyCancelled_DoesNotRunACycle()
        {
            var runner = new CaptureLoopRunner(NullLog.Instance, _ => { });
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            int cycles = 0;
            int stopped = 0;
            int released = 0;

            await runner.RunAsync(
                cts,
                () => 1,
                _ => { cycles++; return Task.CompletedTask; },
                _ => true,
                () => released++,
                () => stopped++,
                () => { });

            Assert.Equal(0, cycles);
            Assert.Equal(1, stopped);
            Assert.Equal(1, released);
        }

        [Fact]
        public async Task RunAsync_AutoPausesAfterConsecutiveFailures()
        {
            var runner = new CaptureLoopRunner(NullLog.Instance, _ => { });
            using var cts = new CancellationTokenSource();
            int cycles = 0;
            int autoPaused = 0;
            int stopped = 0;

            await runner.RunAsync(
                cts,
                () => 1,
                _ =>
                {
                    cycles++;
                    throw new InvalidOperationException("boom");
                },
                _ => true,
                () => { },
                () => stopped++,
                () => autoPaused++);

            Assert.Equal(CaptureLoopRunner.MaxConsecutiveCaptureFailures, cycles);
            Assert.Equal(1, autoPaused);
            Assert.Equal(0, stopped);
        }
    }
}
