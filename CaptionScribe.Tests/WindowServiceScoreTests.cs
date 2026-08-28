using System;
using CaptionScribe.Core.Interop;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class WindowServiceScoreTests
    {
        private static WindowInfo Win(string title, string process)
            => new(IntPtr.Zero, title, process, 0, false);

        [Fact]
        public void NonTeamsWindow_ScoresZero()
        {
            Assert.Equal(0, WindowService.Score(Win("Notepad", "notepad"), null));
        }

        [Fact]
        public void TeamsProcess_OutscoresTitleOnlyMatch()
        {
            int titleOnly = WindowService.Score(Win("my teams notes", "chrome"), null);
            int process = WindowService.Score(Win("Microsoft Teams", "ms-teams"), null);

            Assert.True(titleOnly > 0);
            Assert.True(process > titleOnly);
        }

        [Fact]
        public void MeetingInTitle_BoostsScore()
        {
            int withMeeting = WindowService.Score(Win("Standup | Microsoft Teams meeting", "ms-teams"), null);
            int without = WindowService.Score(Win("Chat | Microsoft Teams", "ms-teams"), null);

            Assert.True(withMeeting > without);
        }

        [Fact]
        public void TitleHint_AddsToScore()
        {
            int withHint = WindowService.Score(Win("Project Sync - Teams", "ms-teams"), "Sync");
            int without = WindowService.Score(Win("Project Sync - Teams", "ms-teams"), null);

            Assert.Equal(without + 30, withHint);
        }

        [Fact]
        public void AllSignals_ProduceTheExpectedTotal()
        {
            // base 1 + process 100 + "teams" in title 20 + "meeting" 40 + hint 30
            int score = WindowService.Score(Win("Weekly Sync | Microsoft Teams meeting", "ms-teams"), "Weekly");
            Assert.Equal(191, score);
        }
    }
}
