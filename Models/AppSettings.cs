namespace CaptionScribe.Models
{
    public sealed class AppSettings
    {
        public CaptureRegion? Region { get; set; }

        /// <summary>How often to grab and OCR the region.</summary>
        public int CaptureIntervalMs { get; set; } = 1500;

        /// <summary>Upscale the captured region before OCR to improve accuracy on small text.</summary>
        public int UpscaleFactor { get; set; } = 2;

        /// <summary>Convert the captured region to high-contrast grayscale before OCR.</summary>
        public bool EnhanceForOcr { get; set; } = true;

        /// <summary>0..1 fuzzy-match threshold used when de-duplicating scrolling caption lines.</summary>
        public double SimilarityThreshold { get; set; } = 0.75;

        /// <summary>When false, the display keeps only the last ~200 lines (full text is still saved).</summary>
        public bool ShowAllOutput { get; set; } = false;

        /// <summary>Prefix transcript lines with their capture time (HH:mm:ss) in the view and on save.</summary>
        public bool ShowTimestamps { get; set; }

        /// <summary>When timestamps are on, show them only at the start of each speaker's turn (less noisy).</summary>
        public bool TimestampsPerTurn { get; set; } = true;

        /// <summary>How often (minutes) to append newly-finalized lines to the autosave file.</summary>
        public int AutoSaveIntervalMinutes { get; set; } = 1;

        /// <summary>Folder for autosave files. Blank uses %APPDATA%\CaptionScribe\autosave.</summary>
        public string AutoSaveDirectory { get; set; } = "";

        /// <summary>Default folder offered by the Save dialog. Blank uses the OS default.</summary>
        public string DefaultSaveDirectory { get; set; } = "";

        /// <summary>Experimental: briefly bring the Teams window to the foreground for each capture.</summary>
        public bool FocusSwitchEnabled { get; set; }

        /// <summary>Delay after activating Teams before the screenshot, to let it render.</summary>
        public int FocusSettleMs { get; set; } = 120;

        /// <summary>Extra title substring used to help locate the Teams meeting window.</summary>
        public string TeamsWindowTitleHint { get; set; } = "Teams";

        /// <summary>Only capture while a Teams window is in front of the region (avoids trailing text after a meeting ends).</summary>
        public bool RequireTeamsWindow { get; set; }

        /// <summary>Write verbose debug entries to the log file (off by default).</summary>
        public bool EnableDebugLogging { get; set; }

        /// <summary>On launch, prompt to clear the autosave folder when it holds more than this many files.</summary>
        public int AutoSavePromptThreshold { get; set; } = 500;

        /// <summary>On launch, automatically delete autosave files older than a month.</summary>
        public bool AutoDeleteOldAutoSaves { get; set; }

        /// <summary>Experimental: on save, also write a participants image (avatars + names) from the capture region.</summary>
        public bool EnableParticipantCapture { get; set; }
    }
}
