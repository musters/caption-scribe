using System;
using CaptionScribe.Models;
using CaptionScribe.ViewModels;
using Xunit;

namespace CaptionScribe.Tests
{
    public class MainViewModelTests
    {
        private sealed class Ctx
        {
            public FakeCaptureController Controller { get; } = new();
            public FakeDialogService Dialogs { get; } = new();
            public FakeRegionService Regions { get; } = new();
            public FakeNotificationService Notifications { get; } = new();
            public FakeSettingsService SettingsService { get; } = new();
            public FakeTextFileWriter Files { get; } = new();
            public FakeParticipantCollector Participants { get; } = new();
            public AppSettings Settings { get; } = new();

            public MainViewModel Build()
                => new(Controller, Settings, SettingsService, Dialogs, Regions, Notifications, Files, Participants);
        }

        private static CaptureRegion SomeRegion() => new() { X = 0, Y = 0, Width = 20, Height = 10 };

        // ---- command availability ----

        [Fact]
        public void SaveClearCopy_DisabledWithoutContent_EnabledWithContent()
        {
            var ctx = new Ctx();
            var vm = ctx.Build();

            Assert.False(vm.SaveCommand.CanExecute(null));
            Assert.False(vm.ClearCommand.CanExecute(null));
            Assert.False(vm.CopyCommand.CanExecute(null));

            ctx.Controller.TranscriptLineCount = 3;

            Assert.True(vm.SaveCommand.CanExecute(null));
            Assert.True(vm.ClearCommand.CanExecute(null));
            Assert.True(vm.CopyCommand.CanExecute(null));
            Assert.True(vm.HasContent);
        }

        [Fact]
        public void StopCommand_EnabledOnlyWhileCapturing()
        {
            var ctx = new Ctx();
            var vm = ctx.Build();

            Assert.False(vm.StopCommand.CanExecute(null));
            ctx.Controller.IsRunning = true;
            Assert.True(vm.StopCommand.CanExecute(null));
        }

        [Fact]
        public void NewScribeCommand_DisabledWhileCapturing()
        {
            var ctx = new Ctx();
            var vm = ctx.Build();

            Assert.True(vm.NewScribeCommand.CanExecute(null));
            ctx.Controller.IsRunning = true;
            Assert.False(vm.NewScribeCommand.CanExecute(null));
        }

        // ---- capture toggling ----

        [Fact]
        public void ToggleActive_StartsCapture_WhenIdleWithRegion()
        {
            var ctx = new Ctx();
            ctx.Settings.Region = SomeRegion();
            var vm = ctx.Build();

            vm.ToggleActive();

            Assert.Equal(1, ctx.Controller.StartCalls);
            Assert.True(vm.IsCapturing);
        }

        [Fact]
        public void ToggleActive_StopsCapture_WhenRunning()
        {
            var ctx = new Ctx();
            ctx.Controller.IsRunning = true;
            var vm = ctx.Build();

            vm.ToggleActive();

            Assert.Equal(1, ctx.Controller.StopCalls);
            Assert.False(vm.IsCapturing);
        }

        [Fact]
        public void ToggleActive_WithNoRegion_PromptsSelection_AndDoesNotStart_WhenCancelled()
        {
            var ctx = new Ctx();   // no region; region picker returns null
            var vm = ctx.Build();

            vm.ToggleActive();

            Assert.Equal(1, ctx.Regions.SelectCalls);
            Assert.Equal(0, ctx.Controller.StartCalls);
        }

        [Fact]
        public void ToggleActive_WithNoRegion_PicksRegion_ThenStarts()
        {
            var ctx = new Ctx();
            ctx.Regions.RegionToReturn = SomeRegion();
            var vm = ctx.Build();

            vm.ToggleActive();

            Assert.Equal(1, ctx.Regions.SelectCalls);
            Assert.NotNull(ctx.Controller.UpdatedRegion);
            Assert.Equal(1, ctx.Controller.StartCalls);
            Assert.True(ctx.SettingsService.SaveCalls >= 1);   // region persisted
        }

        [Fact]
        public void IsActive_Setter_TogglesCapture()
        {
            var ctx = new Ctx();
            ctx.Settings.Region = SomeRegion();
            var vm = ctx.Build();

            vm.IsActive = true;

            Assert.Equal(1, ctx.Controller.StartCalls);
        }

        // ---- clear / new ----

        [Fact]
        public void NewScribe_ClearsTranscript_WhenConfirmed()
        {
            var ctx = new Ctx();
            ctx.Dialogs.ConfirmOkCancelResult = true;
            var vm = ctx.Build();

            vm.NewScribeCommand.Execute(null);

            Assert.Equal(1, ctx.Controller.ClearCalls);
        }

        [Fact]
        public void NewScribe_DoesNothing_WhenCancelled()
        {
            var ctx = new Ctx();
            ctx.Dialogs.ConfirmOkCancelResult = false;
            var vm = ctx.Build();

            vm.NewScribeCommand.Execute(null);

            Assert.Equal(0, ctx.Controller.ClearCalls);
        }

        [Fact]
        public void Clear_ClearsTranscript_WhenConfirmed()
        {
            var ctx = new Ctx();
            ctx.Controller.TranscriptLineCount = 5;
            ctx.Dialogs.ConfirmYesNoResult = true;
            var vm = ctx.Build();

            vm.ClearCommand.Execute(null);

            Assert.Equal(1, ctx.Controller.ClearCalls);
        }

        // ---- copy / about / diagnostics / highlight ----

        [Fact]
        public void Copy_PutsTranscriptTextOnClipboard()
        {
            var ctx = new Ctx();
            ctx.Controller.TranscriptText = "hello world";
            ctx.Controller.TranscriptLineCount = 1;
            var vm = ctx.Build();

            vm.CopyCommand.Execute(null);

            Assert.Equal("hello world", ctx.Dialogs.LastClipboardText);
        }

        [Fact]
        public void About_PassesTheAutoSavePath()
        {
            var ctx = new Ctx();
            ctx.Controller.AutoSavePath = @"C:\meetings\scribe.txt";
            var vm = ctx.Build();

            vm.AboutCommand.Execute(null);

            Assert.Equal(1, ctx.Dialogs.ShowAboutCalls);
            Assert.Equal(@"C:\meetings\scribe.txt", ctx.Dialogs.LastAboutPath);
        }

        [Fact]
        public void CaptureDiagnostics_ShowsTheControllerReport()
        {
            var ctx = new Ctx();
            ctx.Controller.CaptureDiagnostics = "DIAGNOSTICS-REPORT";
            var vm = ctx.Build();

            vm.CaptureDiagnosticsCommand.Execute(null);

            Assert.Equal("DIAGNOSTICS-REPORT", ctx.Dialogs.LastInfoMessage);
        }

        [Fact]
        public void Highlight_WithRegion_HighlightsIt()
        {
            var ctx = new Ctx();
            ctx.Settings.Region = SomeRegion();
            var vm = ctx.Build();

            vm.HighlightCommand.Execute(null);

            Assert.NotNull(ctx.Regions.Highlighted);
        }

        [Fact]
        public void Highlight_WithoutRegion_NotifiesInstead()
        {
            var ctx = new Ctx();
            var vm = ctx.Build();

            vm.HighlightCommand.Execute(null);

            Assert.Single(ctx.Notifications.Infos);
            Assert.Null(ctx.Regions.Highlighted);
        }

        // ---- settings ----

        [Fact]
        public void OpenSettings_AppliesAndSaves_WhenConfirmed()
        {
            var ctx = new Ctx();
            ctx.Dialogs.ShowSettingsResult = true;
            var vm = ctx.Build();

            vm.SettingsCommand.Execute(null);

            Assert.Equal(1, ctx.Controller.ApplySettingsCalls);
            Assert.True(ctx.SettingsService.SaveCalls >= 1);
        }

        [Fact]
        public void OpenSettings_DoesNothing_WhenCancelled()
        {
            var ctx = new Ctx();
            ctx.Dialogs.ShowSettingsResult = false;
            var vm = ctx.Build();

            vm.SettingsCommand.Execute(null);

            Assert.Equal(0, ctx.Controller.ApplySettingsCalls);
            Assert.Equal(0, ctx.SettingsService.SaveCalls);
        }

        // ---- exit / display ----

        [Fact]
        public void ExitCommand_RaisesExitRequested()
        {
            var ctx = new Ctx();
            var vm = ctx.Build();
            bool raised = false;
            vm.ExitRequested += (_, _) => raised = true;

            vm.ExitCommand.Execute(null);

            Assert.True(raised);
        }

        [Fact]
        public void TranscriptText_ShowsPlainTail_ByDefault()
        {
            var ctx = new Ctx();
            ctx.Controller.TranscriptText = "the transcript";
            ctx.Controller.TranscriptLineCount = 1;
            var vm = ctx.Build();

            Assert.Equal("the transcript", vm.TranscriptText);
        }

        [Fact]
        public void TranscriptText_ShowAllOutput_CapsDisplay_WithBanner_WhenHuge()
        {
            var ctx = new Ctx();
            ctx.Settings.ShowAllOutput = true;
            ctx.Controller.TranscriptText = "body";
            ctx.Controller.TranscriptLineCount = 10_001;   // beyond the Show All Output cap
            var vm = ctx.Build();

            Assert.Contains("showing the last 10000 lines", vm.TranscriptText);
            Assert.Contains("body", vm.TranscriptText);
        }

        [Fact]
        public void RegionText_ReflectsWhetherARegionIsSet()
        {
            var ctx = new Ctx();
            var vm = ctx.Build();
            Assert.Contains("not set", vm.RegionText);

            ctx.Settings.Region = SomeRegion();
            var vm2 = ctx.Build();
            Assert.Contains("20", vm2.RegionText);
        }

        // ---- save ----

        [Fact]
        public void Save_AsIs_WritesTranscriptText_ToThePickedPath()
        {
            var ctx = new Ctx();
            ctx.Controller.TranscriptText = "line one\nline two";
            ctx.Controller.TranscriptLineCount = 2;
            ctx.Dialogs.SaveFilePathResult = @"C:\out\meeting.txt";
            var vm = ctx.Build();

            vm.SaveCommand.Execute(null);

            Assert.Equal(1, ctx.Files.Writes);
            Assert.Equal(@"C:\out\meeting.txt", ctx.Files.LastPath);
            Assert.Equal("line one\nline two", ctx.Files.LastContent);
        }

        [Fact]
        public void Save_SuggestsAStampedFileName_FromSessionStartAndTitle()
        {
            var ctx = new Ctx();
            ctx.Controller.TranscriptLineCount = 1;
            ctx.Controller.SessionStartedAt = new DateTime(2026, 8, 26, 9, 30, 0);
            ctx.Dialogs.MeetingTitleResult = "Weekly Sync";
            ctx.Dialogs.SaveFilePathResult = null;   // cancel at the picker; we only check the suggestion
            var vm = ctx.Build();

            vm.SaveCommand.Execute(null);

            Assert.Equal("2026-08-26-Meeting-09-30-Weekly Sync.txt", ctx.Dialogs.LastSuggestedFileName);
        }

        [Fact]
        public void Save_CancelledAtFilePicker_WritesNothing()
        {
            var ctx = new Ctx();
            ctx.Controller.TranscriptLineCount = 1;
            ctx.Dialogs.SaveFilePathResult = null;
            var vm = ctx.Build();

            vm.SaveCommand.Execute(null);

            Assert.Equal(0, ctx.Files.Writes);
        }

        [Fact]
        public void Save_CancelledAtCleanupPrompt_WritesNothing()
        {
            var ctx = new Ctx();
            ctx.Controller.TranscriptLineCount = 1;
            ctx.Dialogs.SaveCleanupResult = null;   // user cancelled the cleanup dialog
            var vm = ctx.Build();

            vm.SaveCommand.Execute(null);

            Assert.Equal(0, ctx.Files.Writes);
        }

        [Fact]
        public void Stop_AfterSuccessfulSave_ClearsTranscript_AndStaysInactive()
        {
            var ctx = new Ctx();
            ctx.Controller.IsRunning = true;
            ctx.Controller.TranscriptLineCount = 3;
            ctx.Dialogs.SaveFilePathResult = @"C:\out\m.txt";
            var vm = ctx.Build();

            vm.StopCommand.Execute(null);

            Assert.Equal(1, ctx.Controller.StopCalls);
            Assert.Equal(1, ctx.Files.Writes);
            Assert.Equal(1, ctx.Controller.ClearCalls);
            Assert.False(vm.IsCapturing);
        }

        [Fact]
        public void Stop_WhenSaveCancelled_DoesNotClear()
        {
            var ctx = new Ctx();
            ctx.Controller.IsRunning = true;
            ctx.Controller.TranscriptLineCount = 3;
            ctx.Dialogs.SaveFilePathResult = null;   // cancel at the picker
            var vm = ctx.Build();

            vm.StopCommand.Execute(null);

            Assert.Equal(1, ctx.Controller.StopCalls);
            Assert.Equal(0, ctx.Files.Writes);
            Assert.Equal(0, ctx.Controller.ClearCalls);
        }

        [Fact]
        public void Save_DuringCapture_WritesButDoesNotStopOrClear()
        {
            var ctx = new Ctx();
            ctx.Controller.IsRunning = true;
            ctx.Controller.TranscriptLineCount = 3;
            ctx.Dialogs.SaveFilePathResult = @"C:\out\m.txt";
            var vm = ctx.Build();

            vm.SaveCommand.Execute(null);

            Assert.Equal(1, ctx.Files.Writes);
            Assert.Equal(0, ctx.Controller.StopCalls);
            Assert.Equal(0, ctx.Controller.ClearCalls);
            Assert.True(vm.IsCapturing);
        }

        // ---- participants capture wiring ----

        [Fact]
        public void ToggleActive_Start_AlsoStartsParticipantCapture()
        {
            var ctx = new Ctx();
            ctx.Settings.Region = SomeRegion();
            ctx.Settings.EnableParticipantCapture = true;
            var vm = ctx.Build();

            vm.ToggleActive();

            Assert.Equal(1, ctx.Participants.StartCalls);
        }

        [Fact]
        public void ToggleActive_Stop_AlsoStopsParticipantCapture()
        {
            var ctx = new Ctx();
            ctx.Controller.IsRunning = true;
            var vm = ctx.Build();

            vm.ToggleActive();

            Assert.Equal(1, ctx.Participants.StopCalls);
        }

        [Fact]
        public void Save_WithParticipants_WritesParticipantsImage_NextToTranscript()
        {
            var ctx = new Ctx();
            ctx.Controller.TranscriptLineCount = 2;
            ctx.Controller.SessionStartedAt = new DateTime(2026, 8, 26, 9, 30, 0);
            ctx.Dialogs.MeetingTitleResult = "Weekly Sync";
            ctx.Dialogs.SaveFilePathResult = @"C:\out\meeting.txt";
            ctx.Participants.Count = 3;
            ctx.Settings.EnableParticipantCapture = true;
            var vm = ctx.Build();

            vm.SaveCommand.Execute(null);

            Assert.Equal(1, ctx.Participants.WriteCalls);
            Assert.Equal(@"C:\out\meeting-Participants.png", ctx.Participants.LastPath);
            Assert.Contains("3 participants", ctx.Participants.LastTitle);
            Assert.Contains("Weekly Sync", ctx.Participants.LastTitle);
            Assert.Contains("2026-08-26", ctx.Participants.LastTitle);
        }

        [Fact]
        public void Save_WithNoParticipants_WritesNoImage()
        {
            var ctx = new Ctx();
            ctx.Controller.TranscriptLineCount = 2;
            ctx.Dialogs.SaveFilePathResult = @"C:\out\meeting.txt";
            ctx.Participants.Count = 0;
            var vm = ctx.Build();

            vm.SaveCommand.Execute(null);

            Assert.Equal(1, ctx.Files.Writes);
            Assert.Equal(0, ctx.Participants.WriteCalls);
        }

        [Fact]
        public void ToggleActive_Start_DoesNotStartParticipants_WhenFeatureDisabled()
        {
            var ctx = new Ctx();
            ctx.Settings.Region = SomeRegion();   // participant capture off by default
            var vm = ctx.Build();

            vm.ToggleActive();

            Assert.Equal(0, ctx.Participants.StartCalls);
        }

        [Fact]
        public void Save_DoesNotWriteParticipantsImage_WhenFeatureDisabled()
        {
            var ctx = new Ctx();
            ctx.Controller.TranscriptLineCount = 2;
            ctx.Dialogs.SaveFilePathResult = @"C:\out\meeting.txt";
            ctx.Participants.Count = 3;   // roster has entries, but the feature is off
            var vm = ctx.Build();

            vm.SaveCommand.Execute(null);

            Assert.Equal(1, ctx.Files.Writes);
            Assert.Equal(0, ctx.Participants.WriteCalls);
        }

        [Fact]
        public void Stop_AfterSuccessfulSave_ResetsParticipants()
        {
            var ctx = new Ctx();
            ctx.Controller.IsRunning = true;
            ctx.Controller.TranscriptLineCount = 3;
            ctx.Dialogs.SaveFilePathResult = @"C:\out\m.txt";
            var vm = ctx.Build();

            vm.StopCommand.Execute(null);

            Assert.Equal(1, ctx.Participants.StopCalls);
            Assert.Equal(1, ctx.Participants.ResetCalls);
        }

        [Fact]
        public void NewScribe_ResetsParticipants_WhenConfirmed()
        {
            var ctx = new Ctx();
            ctx.Dialogs.ConfirmOkCancelResult = true;
            var vm = ctx.Build();

            vm.NewScribeCommand.Execute(null);

            Assert.Equal(1, ctx.Participants.ResetCalls);
        }

        [Fact]
        public void Clear_ResetsParticipants_WhenConfirmed()
        {
            var ctx = new Ctx();
            ctx.Controller.TranscriptLineCount = 5;
            ctx.Dialogs.ConfirmYesNoResult = true;
            var vm = ctx.Build();

            vm.ClearCommand.Execute(null);

            Assert.Equal(1, ctx.Participants.ResetCalls);
        }

        [Fact]
        public void EnableParticipantCapture_Setter_SavesAndSyncsWhileCapturing()
        {
            var ctx = new Ctx();
            ctx.Controller.IsRunning = true;
            var vm = ctx.Build();

            vm.EnableParticipantCapture = true;

            Assert.True(ctx.Settings.EnableParticipantCapture);
            Assert.True(ctx.SettingsService.SaveCalls >= 1);
            Assert.Equal(1, ctx.Participants.StartCalls);

            vm.EnableParticipantCapture = false;
            Assert.Equal(1, ctx.Participants.StopCalls);
        }
    }
}
