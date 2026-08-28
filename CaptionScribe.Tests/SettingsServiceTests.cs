using System;
using System.Collections.Generic;
using System.IO;
using CaptionScribe.Core.Logging;
using CaptionScribe.Models;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class SettingsServiceTests : IDisposable
    {
        private readonly string _dir =
            Path.Combine(Path.GetTempPath(), "CaptionScribeSettings", Guid.NewGuid().ToString("N"));
        private readonly string _appConfigDir =
            Path.Combine(Path.GetTempPath(), "CaptionScribeAppConfig", Guid.NewGuid().ToString("N"));

        public SettingsServiceTests()
        {
            Directory.CreateDirectory(_dir);
            Directory.CreateDirectory(_appConfigDir);
        }

        private SettingsService New(ILog? log = null) => new(log ?? NullLog.Instance, _dir, _appConfigDir);
        private void WriteUserFile(string json) => File.WriteAllText(Path.Combine(_dir, "settings.json"), json);
        private void WriteAppConfig(string json) => File.WriteAllText(Path.Combine(_appConfigDir, "appsettings.json"), json);

        [Fact]
        public void Load_WithNoFiles_ReturnsDefaults()
        {
            var settings = New().Load();

            Assert.Equal(1500, settings.CaptureIntervalMs);
            Assert.Equal(2, settings.UpscaleFactor);
            Assert.Equal(0.75, settings.SimilarityThreshold);
            Assert.False(settings.EnableDebugLogging);
        }

        [Fact]
        public void Load_UserFileOverridesOnlyItsOwnFields()
        {
            WriteUserFile("{ \"CaptureIntervalMs\": 3000 }");

            var settings = New().Load();

            Assert.Equal(3000, settings.CaptureIntervalMs);   // overridden
            Assert.Equal(2, settings.UpscaleFactor);          // still the default
        }

        [Fact]
        public void Load_CorruptUserFile_FallsBackToDefaults_AndWarns()
        {
            WriteUserFile("{ this is not valid json");
            var log = new RecordingLog();

            var settings = New(log).Load();

            Assert.Equal(1500, settings.CaptureIntervalMs);
            Assert.NotEmpty(log.Warnings);
        }

        [Fact]
        public void SaveThenLoad_RoundTripsValues()
        {
            New().Save(new AppSettings
            {
                CaptureIntervalMs = 2222,
                UpscaleFactor = 4,
                EnableDebugLogging = true,
                DefaultSaveDirectory = @"C:\meetings",
            });

            var loaded = New().Load();

            Assert.Equal(2222, loaded.CaptureIntervalMs);
            Assert.Equal(4, loaded.UpscaleFactor);
            Assert.True(loaded.EnableDebugLogging);
            Assert.Equal(@"C:\meetings", loaded.DefaultSaveDirectory);
        }

        [Fact]
        public void Load_AppConfigIsBaseLayer_UserFileWins()
        {
            WriteAppConfig("{ \"CaptionScribe\": { \"CaptureIntervalMs\": 2000, \"UpscaleFactor\": 3 } }");
            WriteUserFile("{ \"CaptureIntervalMs\": 4000 }");

            var settings = New().Load();

            Assert.Equal(4000, settings.CaptureIntervalMs);   // user overrides app config
            Assert.Equal(3, settings.UpscaleFactor);          // from the app-config base layer
            Assert.Equal(0.75, settings.SimilarityThreshold); // default: present in neither file
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
            try { Directory.Delete(_appConfigDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    internal sealed class RecordingLog : ILog
    {
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();

        public bool IsDebugEnabled => false;
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) => Warnings.Add(message);
        public void Error(string message, Exception? exception = null) => Errors.Add(message);
    }
}
