using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    /// <summary>Whether the transcript should be cleaned before saving.</summary>
    public enum SaveCleanup { Clean, AsIs }

    /// <summary>Dialogs the main screen needs, kept behind an interface so view-models stay window-free.</summary>
    public interface IDialogService
    {
        bool ConfirmOkCancel(string title, string message);
        bool ConfirmYesNo(string title, string message);

        /// <summary>Cleanup prompt shown before saving; null means the user cancelled.</summary>
        SaveCleanup? AskSaveCleanup();

        /// <summary>Prompts for the meeting title; null means the user cancelled.</summary>
        string? PromptMeetingTitle();

        /// <summary>Save-file picker; returns the chosen path, or null if cancelled.</summary>
        string? PickSaveFile(string suggestedFileName, string? initialDirectory);

        /// <summary>Folder picker; returns the chosen folder, or null if cancelled.</summary>
        string? PickFolder(string? initialDirectory);

        void Info(string title, string message);
        bool CopyToClipboard(string text);

        bool ShowSettings(AppSettings settings);
        void ShowHelp();
        void ShowAbout(string autoSavePath);
    }
}
