using System;
using CaptionScribe.Core.Logging;
using CaptionScribe.Models;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class CaptureControllerTests
    {
        [Fact]
        public void Start_WithoutRegion_DoesNotRun()
        {
            using var controller = new CaptureController(new AppSettings(), NullLog.Instance);
            controller.Start();
            Assert.False(controller.IsRunning);
        }

        [Fact]
        public void UpdateRegion_AndApplySettings_DoNotThrow()
        {
            var settings = new AppSettings { SimilarityThreshold = 0.8 };
            using var controller = new CaptureController(settings, NullLog.Instance);
            controller.UpdateRegion(new CaptureRegion(1, 2, 10, 10));
            settings.SimilarityThreshold = 0.5;
            controller.ApplySettings();
            Assert.False(controller.IsRunning);
        }

        [Fact]
        public void ClearTranscript_OnEmpty_StaysEmpty()
        {
            using var controller = new CaptureController(new AppSettings(), NullLog.Instance);
            controller.ClearTranscript();
            Assert.Equal(0, controller.TranscriptLineCount);
            Assert.Equal("", controller.GetTranscriptText());
        }

        [Fact]
        public void Stop_WhenIdle_IsIdempotent()
        {
            using var controller = new CaptureController(new AppSettings(), NullLog.Instance);
            controller.Stop();
            controller.Stop();
            Assert.False(controller.IsRunning);
        }

        [Fact]
        public void GetCaptureDiagnostics_ReturnsText()
        {
            using var controller = new CaptureController(new AppSettings(), NullLog.Instance);
            Assert.False(string.IsNullOrWhiteSpace(controller.GetCaptureDiagnostics()));
        }

        [Fact]
        public void GetCaptureDiagnostics_IncludesUpdatedRegion()
        {
            using var controller = new CaptureController(new AppSettings(), NullLog.Instance);
            controller.UpdateRegion(new CaptureRegion(10, 20, 30, 40));
            string report = controller.GetCaptureDiagnostics();
            Assert.Contains("30", report);
            Assert.Contains("40", report);
        }
    }
}
