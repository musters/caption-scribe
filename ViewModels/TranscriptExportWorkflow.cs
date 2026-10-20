using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CaptionScribe.Models;
using CaptionScribe.Services;

namespace CaptionScribe.ViewModels
{
    /// <summary>
    /// Save/copy busy pipeline: one gate, snapshot at entry, dialogs, then write off the UI thread.
    /// </summary>
    internal sealed class TranscriptExportWorkflow
    {
        private readonly ICaptureController _controller;
        private readonly IDialogService _dialogs;
        private readonly ITextFileWriter _fileWriter;
        private readonly IParticipantCollector _participants;
        private readonly AppSettings _settings;
        private readonly Action<bool, string> _setBusy;
        private readonly Action _refresh;
        private int _saveGate;

        public TranscriptExportWorkflow(
            ICaptureController controller,
            IDialogService dialogs,
            ITextFileWriter fileWriter,
            IParticipantCollector participants,
            AppSettings settings,
            Action<bool, string> setBusy,
            Action refresh)
        {
            _controller = controller;
            _dialogs = dialogs;
            _fileWriter = fileWriter;
            _participants = participants;
            _settings = settings;
            _setBusy = setBusy;
            _refresh = refresh;
        }

        public async Task CopyAsync()
        {
            if (Interlocked.CompareExchange(ref _saveGate, 1, 0) != 0)
                return;
            _setBusy(true, "Copying transcript…");
            try
            {
                string text = await Task.Run(() => _controller.GetTranscriptText()).ConfigureAwait(true);
                if (string.IsNullOrEmpty(text))
                    return;
                if (!_dialogs.CopyToClipboard(text))
                    _dialogs.Info("Copy", "Could not copy the transcript to the clipboard.");
            }
            finally
            {
                _setBusy(false, "");
                _refresh();
                Interlocked.Exchange(ref _saveGate, 0);
            }
        }

        public async Task<bool> SaveAsync()
        {
            if (Interlocked.CompareExchange(ref _saveGate, 1, 0) != 0)
                return false;

            _setBusy(true, "Saving transcript…");
            try
            {
                var snapshot = await Task.Run(() => _controller.GetExportSnapshot()).ConfigureAwait(true);
                bool hasAudio = AudioExport.HasUsefulWav(snapshot.AudioWavPath)
                    || AudioExport.HasUsefulWav(snapshot.MicWavPath);
                bool hasParticipants = _settings.EnableParticipantCapture && _participants.Count > 0;
                bool offerFolder = hasAudio || hasParticipants;

                var cleanup = _dialogs.AskSaveCleanup();
                if (cleanup is null)
                    return false;

                var titleChoice = _dialogs.PromptMeetingTitle(offerFolder);
                if (titleChoice is null)
                    return false;

                var suggested = TranscriptExport.SuggestedFileName(snapshot.StartedAt, titleChoice.Value.Title);
                var path = _dialogs.PickSaveFile(suggested, _settings.DefaultSaveDirectory);
                if (path is null)
                    return false;

                if (titleChoice.Value.GroupInFolder && offerFolder)
                    path = MoveIntoMeetingFolder(path, suggested);

                bool timestamps = _settings.ShowTimestamps;
                bool perTurn = _settings.TimestampsPerTurn;
                var cleanupMode = cleanup.Value;
                try
                {
                    await Task.Run(() =>
                    {
                        string text = TranscriptExport.Build(cleanupMode, timestamps, perTurn, snapshot.Timed, snapshot.Plain);
                        _fileWriter.Write(path, text);
                    }).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    _dialogs.Info("Save failed", ex.Message);
                    return false;
                }

                await WriteParticipantsImageAsync(path, titleChoice.Value.Title.Trim(), snapshot.StartedAt).ConfigureAwait(true);
                if (hasAudio)
                    await WriteAudioAsync(snapshot.AudioWavPath, snapshot.MicWavPath, path).ConfigureAwait(true);
                return true;
            }
            finally
            {
                _setBusy(false, "");
                _refresh();
                Interlocked.Exchange(ref _saveGate, 0);
            }
        }

        private static string MoveIntoMeetingFolder(string pickedPath, string suggestedFileName)
        {
            string? parent = Path.GetDirectoryName(pickedPath);
            if (string.IsNullOrEmpty(parent))
                return pickedPath;
            string folder = Path.Combine(parent, Path.GetFileNameWithoutExtension(suggestedFileName));
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, Path.GetFileName(pickedPath));
        }

        private async Task WriteAudioAsync(string? wavPath, string? micWavPath, string transcriptPath)
        {
            string destBase = Path.Combine(
                Path.GetDirectoryName(transcriptPath) ?? "",
                Path.GetFileNameWithoutExtension(transcriptPath));
            try
            {
                await Task.Run(() =>
                {
                    _controller.FlushAudioForExport();
                    AudioExport.TrySaveListeningFile(wavPath, destBase, micWavPath);
                }).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _dialogs.Info("Meeting audio", "Could not save the audio file: " + ex.Message);
            }
        }

        private async Task WriteParticipantsImageAsync(string transcriptPath, string meetingTitle, DateTime started)
        {
            if (!_settings.EnableParticipantCapture || _participants.Count == 0)
                return;
            int count = _participants.Count;
            var pngPath = Path.ChangeExtension(transcriptPath, null) + "-Participants.png";
            var header = $"{meetingTitle} — {started:yyyy-MM-dd} — {started:HH:mm}\n" +
                         $"{count} participant{(count == 1 ? "" : "s")}";
            try
            {
                await Task.Run(() => _participants.WriteImage(pngPath, header)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _dialogs.Info("Participants image", "Could not write the participants image: " + ex.Message);
            }
        }
    }
}
