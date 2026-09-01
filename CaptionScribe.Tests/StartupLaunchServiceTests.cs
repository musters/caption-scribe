using System;
using System.IO;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class StartupLaunchServiceTests : IDisposable
    {
        private readonly string _exe =
            Path.Combine(Path.GetTempPath(), "CaptionScribeStartup", Guid.NewGuid().ToString("N"), "CaptionScribe.exe");
        private readonly MemoryStartupStore _store = new();

        public StartupLaunchServiceTests()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_exe)!);
            File.WriteAllBytes(_exe, new byte[] { 0 });
        }

        public void Dispose()
        {
            try { Directory.Delete(Path.GetDirectoryName(_exe)!, recursive: true); }
            catch { /* temp cleanup */ }
        }

        [Fact]
        public void IsEnabled_WhenApprovedMissing_IsTrueIfRunExists()
        {
            New().SetEnabled(true);
            _store.Approved = null;

            Assert.True(New().IsEnabled());
        }

        [Fact]
        public void IsEnabled_WhenNoRunValue_IsFalse()
        {
            Assert.False(New().IsEnabled());
        }

        [Fact]
        public void IsEnabled_WhenReadFails_IsFalse_AndLogs()
        {
            var log = new RecordingLog();
            _store.ThrowOnGet = true;

            Assert.False(new StartupLaunchService(_store, () => _exe, log).IsEnabled());
            Assert.Contains(log.Warnings, w => w.Contains("Windows startup setting"));
        }

        [Fact]
        public void IsEnabled_WhenStartupApprovedDisabled_IsFalse()
        {
            New().SetEnabled(true);
            _store.Approved = new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            Assert.False(New().IsEnabled());
        }

        private StartupLaunchService New(string? exe = null)
            => new(_store, () => exe ?? _exe);

        [Fact]
        public void SetEnabledFalse_DeletesRunAndApproved()
        {
            New().SetEnabled(true);
            New().SetEnabled(false);

            Assert.Null(_store.RunCommand);
            Assert.Null(_store.Approved);
            Assert.False(New().IsEnabled());
        }

        [Fact]
        public void SetEnabledTrue_WhenApprovedFails_RollsBackRunCommand()
        {
            _store.ThrowOnSetApproved = true;
            Assert.Throws<InvalidOperationException>(() => New().SetEnabled(true));
            Assert.Null(_store.RunCommand);
        }

        [Fact]
        public void SetEnabledTrue_WhenExeMissing_Throws()
        {
            var missing = Path.Combine(Path.GetDirectoryName(_exe)!, "missing.exe");
            Assert.Throws<InvalidOperationException>(() => New(missing).SetEnabled(true));
            Assert.Null(_store.RunCommand);
        }

        [Fact]
        public void SetEnabledTrue_WritesQuotedCommandAndApprovedBlob()
        {
            New().SetEnabled(true);

            Assert.Equal("\"" + _exe + "\" --startup", _store.RunCommand);
            Assert.NotNull(_store.Approved);
            Assert.Equal(0x02, _store.Approved![0]);
            Assert.True(New().IsEnabled());
        }
    }

    internal sealed class MemoryStartupStore : IStartupRegistryStore
    {
        public byte[]? Approved { get; set; }
        public string? RunCommand { get; set; }
        public bool ThrowOnGet { get; set; }
        public bool ThrowOnSetApproved { get; set; }

        public void DeleteApproved() => Approved = null;
        public void DeleteRunCommand() => RunCommand = null;
        public byte[]? GetApproved() => Approved;
        public string? GetRunCommand()
        {
            if (ThrowOnGet)
                throw new InvalidOperationException("read failed");
            return RunCommand;
        }
        public void SetApproved(byte[] data)
        {
            if (ThrowOnSetApproved)
                throw new InvalidOperationException("approved failed");
            Approved = data;
        }
        public void SetRunCommand(string command) => RunCommand = command;
    }
}
