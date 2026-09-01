using System;
using CaptionScribe.Services;

namespace CaptionScribe.Tests
{
    internal sealed class FakeStartupLaunchService : IStartupLaunchService
    {
        public bool Enabled { get; set; }
        public bool? LastSetValue { get; private set; }
        public int SetCalls { get; private set; }
        public bool ThrowOnSet { get; set; }

        public bool IsEnabled() => Enabled;

        public void SetEnabled(bool enabled)
        {
            SetCalls++;
            LastSetValue = enabled;
            if (ThrowOnSet)
                throw new InvalidOperationException("registry failed");
            Enabled = enabled;
        }
    }
}
