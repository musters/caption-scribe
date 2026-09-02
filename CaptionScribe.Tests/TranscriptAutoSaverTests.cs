using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CaptionScribe.Core.Logging;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class TranscriptAutoSaverTests : IDisposable
    {
        private readonly string _dir =
            Path.Combine(Path.GetTempPath(), "CaptionScribeAutoSave", Guid.NewGuid().ToString("N"));

        private readonly List<string> _lines = new();

        private TranscriptAutoSaver NewSaver() =>
            new(() => _lines.ToList(), () => _dir, NullLog.Instance);

        [Fact]
        public void NonFinalFlush_HoldsBackTheStableMargin()
        {
            for (int i = 0; i < 25; i++) _lines.Add($"line {i}");
            var saver = NewSaver();

            saver.Flush(final: false);

            // 25 lines minus the 20-line stable margin = 5 written.
            var written = File.ReadAllLines(saver.CurrentPath);
            Assert.Equal(5, written.Length);
            Assert.Equal("line 0", written[0]);
            Assert.Equal("line 4", written[^1]);
        }

        [Fact]
        public void FinalFlush_WritesEverything()
        {
            for (int i = 0; i < 25; i++) _lines.Add($"line {i}");
            var saver = NewSaver();

            saver.Flush(final: true);

            Assert.Equal(25, File.ReadAllLines(saver.CurrentPath).Length);
        }

        [Fact]
        public void Flush_AppendsOnlyNewLines_NotAlreadyWrittenOnes()
        {
            var saver = NewSaver();
            for (int i = 0; i < 25; i++) _lines.Add($"line {i}");
            saver.Flush(final: true);

            for (int i = 25; i < 30; i++) _lines.Add($"line {i}");
            saver.Flush(final: true);

            var written = File.ReadAllLines(saver.CurrentPath);
            Assert.Equal(30, written.Length);
            Assert.Equal("line 29", written[^1]);
        }

        [Fact]
        public void Roll_StartsANewFile_AndLeavesTheOldOneIntact()
        {
            var saver = NewSaver();
            for (int i = 0; i < 25; i++) _lines.Add($"old {i}");
            saver.Flush(final: true);
            var firstPath = saver.CurrentPath;

            _lines.Clear();
            saver.Roll();
            var secondPath = saver.CurrentPath;
            for (int i = 0; i < 3; i++) _lines.Add($"new {i}");
            saver.Flush(final: true);

            Assert.NotEqual(firstPath, secondPath);
            Assert.Equal(25, File.ReadAllLines(firstPath).Length);
            var second = File.ReadAllLines(secondPath);
            Assert.Equal(3, second.Length);
            Assert.Equal("new 0", second[0]);
        }

        [Fact]
        public void Flush_WithNothingNew_DoesNotThrow_OrCreateContent()
        {
            var saver = NewSaver();
            saver.Flush(final: true);

            var written = File.Exists(saver.CurrentPath) ? File.ReadAllLines(saver.CurrentPath) : Array.Empty<string>();
            Assert.Empty(written);
        }

        // Guards the ClearTranscript ordering: finalize the old file (writing the held-back tail)
        // before rolling, so the previous transcript is complete on disk.
        [Fact]
        public void FinalFlushThenRoll_PreservesTheHeldBackTail_InTheOldFile()
        {
            var saver = NewSaver();
            for (int i = 0; i < 25; i++) _lines.Add($"line {i}");

            saver.Flush(final: false);   // periodic autosave writes 5, holds back the 20-line margin
            var oldPath = saver.CurrentPath;
            Assert.Equal(5, File.ReadAllLines(oldPath).Length);

            saver.Flush(final: true);    // finalize before clearing
            _lines.Clear();
            saver.Roll();

            Assert.Equal(25, File.ReadAllLines(oldPath).Length);   // the held-back tail is not lost
            Assert.NotEqual(oldPath, saver.CurrentPath);
        }

        [Fact]
        public void DirectoryFor_Blank_ReturnsDefaultUnderAppData()
        {
            var path = TranscriptAutoSaver.DirectoryFor("   ");
            Assert.Contains("CaptionScribe", path);
            Assert.EndsWith("autosave", path);
        }

        [Fact]
        public void DirectoryFor_Configured_ReturnsTrimmedPath()
        {
            Assert.Equal(@"C:\my saves", TranscriptAutoSaver.DirectoryFor(@"  C:\my saves  "));
        }

        [Fact]
        public void ClearDirectory_DeletesOnlyScribeFiles()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "scribe-1.txt"), "a");
            File.WriteAllText(Path.Combine(_dir, "scribe-2.txt"), "b");
            File.WriteAllText(Path.Combine(_dir, "notes.txt"), "keep");

            int removed = TranscriptAutoSaver.ClearDirectory(_dir);

            Assert.Equal(2, removed);
            Assert.Empty(Directory.GetFiles(_dir, "scribe-*.txt"));
            Assert.True(File.Exists(Path.Combine(_dir, "notes.txt")));
        }

        [Fact]
        public void ClearDirectory_MissingFolder_ReturnsZero()
        {
            Assert.Equal(0, TranscriptAutoSaver.ClearDirectory(Path.Combine(_dir, "does-not-exist")));
        }

        [Fact]
        public void CountSaves_CountsOnlyScribeFiles()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "scribe-1.txt"), "a");
            File.WriteAllText(Path.Combine(_dir, "scribe-2.txt"), "b");
            File.WriteAllText(Path.Combine(_dir, "notes.txt"), "x");

            Assert.Equal(2, TranscriptAutoSaver.CountSaves(_dir));
        }

        [Fact]
        public void DeleteSavesOlderThan_RemovesOld_KeepsRecent()
        {
            Directory.CreateDirectory(_dir);
            var old = Path.Combine(_dir, "scribe-old.txt");
            var recent = Path.Combine(_dir, "scribe-new.txt");
            File.WriteAllText(old, "a");
            File.WriteAllText(recent, "b");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-40));

            int removed = TranscriptAutoSaver.DeleteSavesOlderThan(_dir, TranscriptAutoSaver.DefaultRetention);

            Assert.Equal(1, removed);
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
