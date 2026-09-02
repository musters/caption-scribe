using CaptionScribe.Models;
using CaptionScribe.Services;

namespace CaptionScribe.Tests
{
    /// <summary>Configurable, recording <see cref="IDialogService"/> for view-model tests.</summary>
    internal sealed class FakeDialogService : IDialogService
    {
        public string? FolderToReturn { get; set; }

        public bool ConfirmOkCancelResult { get; set; } = true;
        public bool ConfirmYesNoResult { get; set; } = true;
        public SaveCleanup? SaveCleanupResult { get; set; } = SaveCleanup.AsIs;
        public string? MeetingTitleResult { get; set; } = "Test";
        public string? SaveFilePathResult { get; set; }
        public bool ShowSettingsResult { get; set; }

        public int ShowSettingsCalls { get; private set; }
        public int ShowHelpCalls { get; private set; }
        public int ShowAboutCalls { get; private set; }
        public string? LastAboutPath { get; private set; }
        public string? LastClipboardText { get; private set; }
        public string? LastInfoTitle { get; private set; }
        public string? LastInfoMessage { get; private set; }
        public string? LastSuggestedFileName { get; private set; }

        public bool ConfirmOkCancel(string title, string message) => ConfirmOkCancelResult;
        public bool ConfirmYesNo(string title, string message) => ConfirmYesNoResult;
        public SaveCleanup? AskSaveCleanup() => SaveCleanupResult;
        public string? PromptMeetingTitle() => MeetingTitleResult;
        public string? PickSaveFile(string suggestedFileName, string? initialDirectory)
        {
            LastSuggestedFileName = suggestedFileName;
            return SaveFilePathResult;
        }
        public string? PickFolder(string? initialDirectory) => FolderToReturn;
        public void Info(string title, string message) { LastInfoTitle = title; LastInfoMessage = message; }
        public bool CopyToClipboard(string text) { LastClipboardText = text; return true; }
        public bool ShowSettings(AppSettings settings) { ShowSettingsCalls++; return ShowSettingsResult; }
        public void ShowHelp() { ShowHelpCalls++; }
        public void ShowAbout(string autoSavePath) { ShowAboutCalls++; LastAboutPath = autoSavePath; }
    }
}
