using System;
using CaptionScribe.Core.Shell;
using Xunit;

namespace CaptionScribe.Tests
{
    public class SingleInstanceHostTests
    {
        [Fact]
        public void TryClaim_SecondInstance_Fails_AndCanNotifyTheFirst()
        {
            string id = Guid.NewGuid().ToString("N");
            string mutex = @"Local\CaptionScribeTest-" + id;
            string ev = @"Local\CaptionScribeTestShow-" + id;

            using var first = new SingleInstanceHost(mutex, ev);
            using var second = new SingleInstanceHost(mutex, ev);

            Assert.True(first.TryClaim(notifyExisting: false));
            Assert.False(second.TryClaim(notifyExisting: true));
        }
    }
}
