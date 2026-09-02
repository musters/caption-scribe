using System;
using System.IO;
using System.Windows;
using CaptionScribe.Core.Interop;
using CaptionScribe.Core.Logging;
using CaptionScribe.Models;
using CaptionScribe.Services;
using CaptionScribe.ViewModels;
using WinForms = System.Windows.Forms;

namespace CaptionScribe.Views
{
    /// <summary>WPF/WinForms implementation of <see cref="IDialogService"/>.</summary>
    public sealed class WpfDialogService : IDialogService
    {
        private readonly ILog _log;
        private readonly IStartupLaunchService _startup;

        public WpfDialogService(IStartupLaunchService startup, ILog log)
        {
            _log = log;
            _startup = startup;
        }

        /// <summary>Owner for modal dialogs; used only while it is visible (the app can be tray-only).</summary>
        public Window? Owner { get; set; }

        private Window? ActiveOwner => Owner is { IsVisible: true } ? Owner : null;

        public SaveCleanup? AskSaveCleanup()
        {
            int choice = ShowButtons(
                "Save Scribe",
                "Clean up the transcript before saving?\n\n" +
                "\u2022 Fix apostrophes and OCR letter/number mix-ups\n" +
                "\u2022 Collapse repeated speaker-name lines",
                ("Clean Up", true, false), ("Save As-Is", false, false), ("Cancel", false, true));
            return choice switch
            {
                0 => SaveCleanup.Clean,
                1 => SaveCleanup.AsIs,
                _ => null,
            };
        }

        public bool ConfirmOkCancel(string title, string message)
            => ShowButtons(title, message, ("OK", true, false), ("Cancel", false, true)) == 0;

        public bool ConfirmYesNo(string title, string message)
            => ShowButtons(title, message, ("Yes", true, false), ("No", false, true)) == 0;

        public bool CopyToClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (Exception ex)
            {
                _log.Warning("Clipboard copy failed. " + ex.Message);
                return false;
            }
        }

        // Best-matching Teams window's title right now (for the Settings "Detect" button), or null.
        private static string? DetectTeamsWindowTitle()
        {
            IntPtr hWnd = new WindowService().FindTeamsWindow(titleHint: null);
            if (hWnd == IntPtr.Zero)
                return null;
            string title = Win32.GetWindowTitle(hWnd);
            return string.IsNullOrWhiteSpace(title) ? null : title;
        }

        public void Info(string title, string message)
            => ShowButtons(title, message, ("OK", true, true));

        public string? PickFolder(string? initialDirectory)
        {
            using var dialog = new WinForms.FolderBrowserDialog();
            if (!string.IsNullOrWhiteSpace(initialDirectory))
                dialog.SelectedPath = initialDirectory;
            return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.SelectedPath : null;
        }

        public string? PickSaveFile(string suggestedFileName, string? initialDirectory)
        {
            using var dialog = new WinForms.SaveFileDialog
            {
                Filter = "Text file (*.txt)|*.txt|Markdown (*.md)|*.md",
                FileName = suggestedFileName,
            };
            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
                dialog.InitialDirectory = initialDirectory;
            return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.FileName : null;
        }

        public string? PromptMeetingTitle()
        {
            var dialog = new InputDialog("Meeting title (used in the file name):", "Save Scribe");
            Prepare(dialog);
            return dialog.ShowDialog() == true ? dialog.ResponseText : null;
        }

        public void ShowAbout(string autoSavePath)
        {
            var dialog = new AboutWindow { DataContext = new AboutViewModel(autoSavePath) };
            Prepare(dialog);
            dialog.ShowDialog();
        }

        private int ShowButtons(string title, string message,
            params (string Text, bool IsDefault, bool IsCancel)[] buttons)
        {
            var dialog = new MessageDialog(title, message, buttons);
            Prepare(dialog);
            dialog.ShowDialog();
            return dialog.Result;
        }

        public void ShowHelp()
        {
            var dialog = new HelpWindow();
            Prepare(dialog);
            dialog.ShowDialog();
        }

        public bool ShowSettings(AppSettings settings)
        {
            var vm = new SettingsViewModel(settings, this, DetectTeamsWindowTitle, _startup, _log);
            var dialog = new SettingsWindow { DataContext = vm };
            void OnClose(object? _, bool result) => dialog.DialogResult = result;
            vm.CloseRequested += OnClose;
            try
            {
                Prepare(dialog);
                return dialog.ShowDialog() == true;
            }
            finally
            {
                vm.CloseRequested -= OnClose;
            }
        }

        private void Prepare(Window dialog)
        {
            if (ActiveOwner is { } owner)
                dialog.Owner = owner;
            else
                dialog.Topmost = true;
        }
    }
}
