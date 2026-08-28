using System;
using System.IO;
using System.Linq;
using CaptionScribe.Models;
using CaptionScribe.ViewModels;
using Xunit;

namespace CaptionScribe.Tests
{
    public class SettingsViewModelTests
    {
        private static SettingsViewModel New(AppSettings? settings = null, Func<string?>? detectTeamsTitle = null)
            => new(settings ?? new AppSettings(), new FakeDialogService(), detectTeamsTitle ?? (() => null));

        [Fact]
        public void FreshFromValidSettings_SaveDisabled_NoErrors()
        {
            var vm = New();
            Assert.False(vm.HasErrors);
            Assert.False(vm.SaveCommand.CanExecute(null));
        }

        [Fact]
        public void ChangingAField_EnablesSave()
        {
            var vm = New();
            vm.CaptureInterval = "2000";
            Assert.True(vm.SaveCommand.CanExecute(null));
        }

        [Fact]
        public void DetectTeamsTitle_WhenFound_SetsHint_AndEnablesSave()
        {
            var vm = New(detectTeamsTitle: () => "Sprint Demo | Microsoft Teams");

            vm.DetectTeamsTitleCommand.Execute(null);

            Assert.Equal("Sprint Demo | Microsoft Teams", vm.Hint);
            Assert.True(vm.SaveCommand.CanExecute(null));
        }

        [Fact]
        public void DetectTeamsTitle_WhenNoneFound_KeepsHint_AndInforms()
        {
            var settings = new AppSettings { TeamsWindowTitleHint = "keep-me" };
            var dialogs = new FakeDialogService();
            var vm = new SettingsViewModel(settings, dialogs, () => null);

            vm.DetectTeamsTitleCommand.Execute(null);

            Assert.Equal("keep-me", vm.Hint);
            Assert.Equal("Detect Teams window", dialogs.LastInfoTitle);
        }

        [Fact]
        public void ChangingAutoSaveCacheSettings_EnablesSave_AndApplies()
        {
            var settings = new AppSettings();
            var vm = New(settings);
            vm.AutoSavePromptThreshold = "250";
            vm.AutoDeleteOldAutoSaves = true;
            Assert.True(vm.SaveCommand.CanExecute(null));

            vm.SaveCommand.Execute(null);

            Assert.Equal(250, settings.AutoSavePromptThreshold);
            Assert.True(settings.AutoDeleteOldAutoSaves);
        }

        [Fact]
        public void InvalidAutoSavePromptThreshold_DisablesSave()
        {
            var vm = New();
            vm.AutoSavePromptThreshold = "0";
            Assert.True(vm.HasErrors);
            Assert.False(vm.SaveCommand.CanExecute(null));
        }

        [Fact]
        public void RevertingAField_DisablesSaveAgain()
        {
            var vm = New(new AppSettings { CaptureIntervalMs = 1500 });
            vm.CaptureInterval = "2000";
            vm.CaptureInterval = "1500";
            Assert.False(vm.SaveCommand.CanExecute(null));
        }

        [Fact]
        public void InvalidValue_SetsErrorAndDisablesSave()
        {
            var vm = New();
            vm.CaptureInterval = "0";

            Assert.True(vm.HasErrors);
            Assert.False(vm.SaveCommand.CanExecute(null));
            var message = vm.GetErrors(nameof(SettingsViewModel.CaptureInterval)).Cast<string>().Single();
            Assert.Contains("between 200 and 60000", message);
        }

        [Fact]
        public void Save_AppliesValues_AndRequestsClose()
        {
            var settings = new AppSettings();
            var vm = New(settings);
            bool? result = null;
            vm.CloseRequested += (_, r) => result = r;

            vm.CaptureInterval = "3000";
            vm.ShowTimestamps = true;
            vm.SaveCommand.Execute(null);

            Assert.Equal(3000, settings.CaptureIntervalMs);
            Assert.True(settings.ShowTimestamps);
            Assert.True(result);
        }

        [Fact]
        public void ClearAutoSave_WhenConfirmed_DeletesScribeFiles()
        {
            var dir = Path.Combine(Path.GetTempPath(), "CaptionScribeVmClear", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "scribe-1.txt"), "x");
            try
            {
                var dialogs = new FakeDialogService { ConfirmYesNoResult = true };
                var vm = new SettingsViewModel(new AppSettings(), dialogs, () => null) { AutoSaveDir = dir };

                vm.ClearAutoSaveCommand.Execute(null);

                Assert.Empty(Directory.GetFiles(dir, "scribe-*.txt"));
                Assert.NotNull(dialogs.LastInfoMessage);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void ClearAutoSave_WhenCancelled_KeepsFiles()
        {
            var dir = Path.Combine(Path.GetTempPath(), "CaptionScribeVmClear", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "scribe-1.txt"), "x");
            try
            {
                var dialogs = new FakeDialogService { ConfirmYesNoResult = false };
                var vm = new SettingsViewModel(new AppSettings(), dialogs, () => null) { AutoSaveDir = dir };

                vm.ClearAutoSaveCommand.Execute(null);

                Assert.Single(Directory.GetFiles(dir, "scribe-*.txt"));
            }
            finally { Directory.Delete(dir, recursive: true); }
        }
    }
}
