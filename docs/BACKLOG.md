# Backlog

Living list of work not yet scheduled. IDs are stable; do not reuse.

- **NEW-####** — new product requirements
- **DEFER-####** — known issues we are postponing
- **AUDIT-####** — findings from code audits

**Status:** `OPEN` | `DONE`  
**Priority:** `LOW` | `MED` | `HIGH`

---

## NEW

Spec: [`docs/ideas/Audio-Transcript-Requirements.md`](ideas/Audio-Transcript-Requirements.md). Implement in ID order. No extra NuGet; Microsoft / Windows / .NET / WASAPI / Media Foundation only.

**Vision:** OCR captions stay the live transcript. Audio is a **sidecar black box** for the same scribe: while Play is on, we record what this PC is playing (and later the mic) into crash-safe WAV next to `scribe-*.txt`. Pause does not punch a hole in the recording. Stop/Save can take home an MP3 beside the `.txt`. Nothing leaves the machine. If audio fails, captions still work.

### NEW-0001 — Crash-safe PCM autosave writer

**Status:** DONE  
**Priority:** HIGH

**Where:** `Services/` (new writer beside `TranscriptAutoSaver`), startup cache in `App.xaml.cs`

**What:** Text already survives a crash via `scribe-yyyyMMdd-HHmmss.txt` in `%APPDATA%\CaptionScribe\autosave` (or the user’s autosave folder). Audio must survive the same way: a kill mid-meeting leaves a **playable** partial WAV, not a corrupt header.

**Vision:** One writer, no WASAPI. Callers give a path and PCM frames. The file is a standard RIFF WAVE (PCM 16-bit). Header sizes are a lie until flush; we rewrite them so Explorer/Media Player can open the file after a crash. Same stem as the text autosave for that scribe (`scribe-20261020-143015.txt` + `scribe-20261020-143015.wav`). Mic, when it exists, is `scribe-20261020-143015-mic.wav`.

**Fix:**
- Resolve directory exactly as `TranscriptAutoSaver.DirectoryFor` (create it; never `%TEMP%`).
- Open/create WAV, append frames, **flush to disk every few seconds** (named constant, ~3 s) and rewrite `RIFF`/`data` chunk sizes from the current file length.
- Close must write a final header. Dispose must flush+close without blocking the UI for more than a brief lock (IO on a worker, same spirit as autosave Stop).
- Startup: scan autosave `scribe-*.wav`; if header size ≠ file length, rewrite header (repair). Skip files that are not PCM WAV.
- Retention: always delete autosave `scribe-*.wav` (and `scribe-*-mic.wav`) with `LastWriteTimeUtc` older than **7 days**, even when 30-day text cleanup is off. Do **not** delete audio the user saved next to a transcript (only the autosave folder, `scribe-` prefix).
- Cache prompt: count WAV as well as txt; “Clear them now?” deletes matching WAV too.
- Unit tests without audio devices: write, simulate unclosed file, repair, assert header; age-out; ignore non-`scribe-` names.

**Out of this item:** capture, encode, UI.

### NEW-0002 — Meeting loopback capture session

**Status:** DONE  
**Priority:** HIGH

**Where:** new audio capture type; session lifecycle next to `CaptureController`; Settings menu (same pattern as Capture Participants)

**What:** Device **render loopback** (WASAPI shared mode, default playback device) while a scribe is capturing. This is “what I heard in the meeting,” including remote voices and shared video sound — and also notifications if they play on that device.

**Vision:** Settings ▸ **Capture Audio**, off by default, persisted. When it is on, Play starts captions **and** loopback; Pause/Stop/tray match captions. The WAV in autosave grows for the whole scribe. OCR never waits on audio. No hooks into Teams.

**Fix:**
- Master toggle only in this item (mic/process-loopback settings come in 0005/0006). Off → no audio objects, no files.
- Start/stop loopback with caption Start/Stop. Auto-pause of captions stops audio. Idle (not capturing) never records.
- Mid-session: toggle **off** stops audio for the rest of this run (captions continue). Toggle **on** starts from now (no backfill).
- Pause: same WAV; stop writing; on Play append **750 ms** of silence (named constant; not wall-clock pause), then PCM.
- Clear / New Scribe: close current WAV, new stem with the new text autosave, **audio epoch++** so in-flight buffers do not append to the new file (same rule as transcript epoch).
- Join/cancel audio off the UI thread; pause must not flush WAV on the dispatcher.
- Fail-soft: no device, exclusive-mode silence, or WASAPI error → captions keep running; do not show “capturing audio” (NEW-0003 wording). Log a warning.
- Not `SetWindowsHookEx`. Shared-mode loopback only.
- Help/README: what loopback records (this PC’s playback), local only, off by default, OCR still primary.

**Out of this item:** mic, process loopback, meter, MP3 encode.

### NEW-0003 — Audio status and level meter

**Status:** DONE  
**Priority:** MED

**Where:** main-window status bar; `MainViewModel` status text

**What:** Without this, Capture Audio is invisible — user cannot tell record vs fail vs pause.

**Vision:** Status line stays the single source of “what is happening.” A small meter is a live pulse, not a mixer console.

**Fix:**
- Captions only: keep `Capturing… Press Spacebar to Pause.`
- Captions + audio actually writing: `Capturing captions and audio… Press Spacebar to Pause.`
- Captions on, audio enabled but not writing (failed / no device): `Capturing… Audio unavailable.` Never imply recording.
- Pause/idle: existing Idle / region messages; no audio claim; meter hidden or grey and still.
- Meter: compact WPF bars/pulse on the status bar, peak = **max of streams that are actually capturing** (loopback; mic too after NEW-0005). Coalesce to the UI like transcript refresh (few Hz). Live only while writing PCM.
- Window already moves off the capture region; do not put the meter in a top-most overlay over captions.
- Help/README: status strings and that the meter means audio is recording now.

### NEW-0004 — Save audio with the transcript

**Status:** DONE  
**Priority:** HIGH

**Where:** `TranscriptExportWorkflow`; save dialogs; Media Foundation (no extra encoder)

**What:** Autosave WAV is the crash net. Stop/Save is how the user **takes the meeting home**.

**Vision:** Same title/timestamp as today’s `.txt`. If this scribe recorded audio, they get one listening file: prefer `.mp3`, else M4A/AAC, else `.wav`. Several outputs → optional folder named like the meeting. Encode on a worker; UI stays busy like a long transcript save.

**Fix:**
- Snapshot autosave audio path(s) at **save-gate** with the transcript (before dialogs). Stop+Save still must not clear if capture restarted.
- “Nothing useful” (missing file, only header, encode failed): skip audio; no folder checkbox unless participants PNG (or other sidecar) exists.
- Encode off the UI thread: MF MP3 sink if present → `.mp3`; else M4A/AAC; else copy WAV. No LAME, no NuGet.
- One **mixed** playback file. Only loopback → encode that WAV. Loopback + mic (NEW-0005) → mix then encode (mic into both stereo channels, clip-safe). Do not ship two user-facing audio files in v1.
- Title prompt: if more than one output, checkbox **Save all files in a folder named after this meeting**, default **checked**. Folder name = existing suggested stem (`yyyy-MM-dd-Meeting-HH-mm-Title`) under the picked location; files inside use short names with that stem. Unchecked: same directory, same stem, different extensions.
- Parent folder is still default-save / picker; checkbox only groups.
- Autosave WAV stays in the autosave folder (like `.txt`) until 7-day cleanup; save copies/encodes out, it does not move the crash file away mid-meeting unless Stop+Save clears the scribe (then the next scribe gets a new stem).
- Help/README: sidecar audio, format fallback, folder checkbox.

**Out of this item:** STT export.

### NEW-0005 — Microphone capture stream

**Status:** DONE  
**Priority:** MED

**Where:** audio capture service; Settings dialog; `app.manifest` microphone capability

**What:** Loopback is everyone **in the speakers**. The mic is **you**. Kept as a separate temp WAV so we can mix at save and, later, label speaker-side.

**Vision:** Still one master switch (Capture Audio). A Settings checkbox **Record microphone**, off by default. First enable → Windows consent. Deny → meeting loopback and OCR continue; no nag beyond a once-per-session log/status. Temp file `scribe-…-mic.wav` in autosave, same flush/header/7-day rules as NEW-0001.

**Fix:**
- Manifest mic capability + runtime consent. Loopback does not require it.
- Do not mix into the loopback WAV while recording.
- Same Play/Pause/Stop/epoch/pause-gap as loopback (750 ms silence on the mic file too).
- Denial or missing mic is fail-soft.
- Windows AEC/DSP if easily available so the user’s voice is not loud on both files; do not block the item if AEC is absent (document).
- Help/README: mic is optional, consent, what it records, still local.

**Out of this item:** STT, per-speaker files at save.

### NEW-0006 — Teams process loopback

**Status:** DONE  
**Priority:** MED

**Where:** audio capture service; `WindowService` Teams process detection

**What:** Device loopback includes Slack pings, YouTube, etc. Process loopback records **Teams’** playback when we can see the process.

**Vision:** Capture Audio still means “record the meeting.” If we already know the Teams process (same detection as focus-switch / require-Teams), use `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`. If not found or activation fails, keep device loopback and stay fail-soft. Never inject into Teams.

**Fix:**
- Prefer process loopback when a Teams PID is available; else device loopback (NEW-0002).
- Failure or empty process capture → fall back to device; captions unaffected.
- If we believe loopback is running but peaks stay ~0 for a long stretch, surface via NEW-0003 (`Audio unavailable` or a quieter “no meeting audio”) — exclusive-mode apps often never appear in loopback.
- Help/README: process vs whole-device; Teams is the target, not a bot.

**Out of this item:** Zoom/other process targeting (later).

---

## DEFER

### DEFER-0001 — Auto-pause: Pause can act like Play

**Status:** DONE  
**Priority:** HIGH

**Where:** `Services/CaptureController.cs` (`Start`/`Stop`/`AfterJoin`), `ViewModels/MainViewModel.cs` (`ToggleActive`), `App.xaml.cs` (tray Play/Pause)

**What:** Loop exit no longer clears `_running` before `AutoPause`→`Stop()`, but Pause is still `ToggleActive()`. After `Stop()`, `IsCapturing` is already false while the Pause icon/tray item can lag, so a Pause click (toolbar, Space, tray Pause→`PlayPauseCommand`) calls `Start()`. `Start()`’s `AfterJoin` also ignores a Pause that happens while the previous loop is joining.

**Fix:** Pause always `Stop()` (idempotent). Tray Pause must not share the Play toggle. Toggle uses last UI-published capturing state, not live `IsRunning`. Invalidate a queued `AfterJoin`/`BeginCapture` on `Stop`/`Dispose`.

---

## AUDIT

Performance and scalability first, then correctness / quality. Hour-long meetings (caption strip vs full-window region) are the scale target.

### AUDIT-0001 — No pixel cap on region × upscale

**Status:** DONE  
**Priority:** HIGH

**Where:** `Views/RegionSelectorForm.cs` (min 5×5, no max), `Models/AppSettings.cs` (upscale 1–4), `Services/ScreenCaptureService.cs`, `Services/OcrService.cs`, `Services/CaptureController.cs`

**What:** Processed buffer is `width * scale` × `height * scale` at 32bpp, then OCR copies it. A 4K region at upscale 2 is tens of MB per buffer; upscale 4 is ~130 MB. Cost is unbounded for the whole meeting. When OCR overruns the interval, the next `PeriodicTimer` tick is already due.

**Fix:** Clamp processed pixels (max edge or megapixels) before `Process`/`Recognize`; reject or downscale oversized regions.

### AUDIT-0002 — Show All Output rebuilds a 10k-line TextBox every OCR

**Status:** DONE  
**Priority:** HIGH

**Where:** `ViewModels/MainViewModel.cs` (`AllOutputLineCount`, `BuildDisplayText`, `Refresh`), `Views/MainWindow.xaml` / `MainWindow.xaml.cs`

**What:** Every caption change on the UI thread formats up to 10 000 wrapped lines, assigns `TextBox.Text`, then `CaretIndex` + `ScrollToEnd`. Coalescing only drops bursts before the dispatcher runs; sequential OCR still refreshes once per cycle. Default 200-line tail is fine; Show All over a 5–8 hour meeting is not.

**Fix:** Virtualize, or keep a 200-line live view and treat Show All as a paused snapshot; throttle UI to a few Hz; avoid replacing the entire `TextBox` document.

### AUDIT-0003 — Pixel fingerprint OCRs almost every tick if the region includes motion

**Status:** DONE  
**Priority:** HIGH

**Where:** `Services/ScreenCaptureService.cs` (`HasMeaningfulChange`), `Services/CaptureController.cs` (`RecognizeCaptionsAsync`)

**What:** Raw luma compare is tighter (`n/40`, min luma delta 12) and unchanged OCR text skips `AddSnapshot`, but that skip is *after* OCR. Video/caret motion that beats the raw threshold still upscales + OCRs every interval.

**Fix:** Keep the raw gate; also fingerprint the processed/grayscale OCR image and skip `Recognize` when that has not changed.

### AUDIT-0004 — New SoftwareBitmap (full copy) every OCR

**Status:** DONE  
**Priority:** MED

**Where:** `Services/OcrService.cs` vs pooled GDI in `ScreenCaptureService.cs`

**What:** `new SoftwareBitmap` + row `MemoryCopy` every changed frame, then dispose. Caption strips are ~MB; a window-sized region is tens of MB, thousands of times per meeting.

**Fix:** Pool one `SoftwareBitmap` by size (same idea as `_processedPool`) and overwrite via `LockBuffer`.

### AUDIT-0005 — No backpressure when OCR exceeds the capture interval

**Status:** DONE  
**Priority:** MED

**Where:** `Services/CaptureController.cs` loop, `AppSettings.MinCaptureIntervalMs` (200)

**What:** `PeriodicTimer` returns immediately if the previous cycle overran. Work is sequential (good for the bitmap pool) but duty cycle becomes 100% for the rest of the meeting.

**Fix:** If recognize overruns, skip grabs until the next interval (or floor the interval from last OCR duration). Do not allow 200 ms on huge regions.

### AUDIT-0006 — Participant roster never expires 1-vote OCR junk

**Status:** DONE  
**Priority:** MED

**Where:** `Services/ParticipantRoster.cs`, `CaptureController` ~4 s `OnFrame` on the capture thread

**What:** Snapshot/count require 2 votes, but `_entries` keeps every name-shaped line that produced an avatar. Each new hit PNG-encodes under `_gate`. `Find` is O(entries) and recomputes `BestName` per compare. False Title-Case lines accumulate for hours.

**Fix:** Expire entries with `TotalVotes < 2` after N samples; cap roster size; cache `BestName`; skip PNG until the second vote; time-box `OnFrame`.

### AUDIT-0007 — Copy builds the full transcript on the UI thread under the aggregator lock

**Status:** DONE  
**Priority:** MED

**Where:** `ViewModels/MainViewModel.cs` `Copy()`, `TranscriptAggregator.GetText()`

**What:** Save already uses `Task.Run`; Copy still `string.Join`s every line while holding `_gate`. An 8 hour transcript is ~10k lines. Capture `AddSnapshot` waits on the same lock. `Clipboard.SetText` of that size can hitch.

**Fix:** Join off the UI thread (same as save), then set the clipboard on the UI thread; or copy from the autosave file.

### AUDIT-0008 — Aggregator lock is held across LCS / Levenshtein

**Status:** DONE  
**Priority:** MED

**Where:** `Services/TranscriptAggregator.cs` `AddSnapshot` / `Merge`

**What:** LCS is windowed (does not grow with meeting length) but a large snapshot (whole-window OCR, 50–100 lines) can hold `_gate` for tens–hundreds of ms and stall UI tail + autosave.

**Fix:** Cap OCR lines fed to `AddSnapshot`; bail if `a*b` exceeds a budget; exact-match first, fuzzy only near the diagonal.

### AUDIT-0009 — Autosave copies the entire line list every flush

**Status:** DONE  
**Priority:** LOW

**Where:** `Services/TranscriptAutoSaver.cs` `Flush`, `TranscriptAggregator.GetLines`

**What:** Every minute: `ToList()` of all n strings, then append `n - 20`. Cheap at 10k lines. Two overlapping flushes (slow OneDrive path) can duplicate a slice.

**Fix:** `GetLines(start, count)` without copying the prefix; single-flight around IO.

### AUDIT-0010 — Default 200-line TextBox is still a full document swap

**Status:** DONE  
**Priority:** LOW

**Where:** `MainViewModel.BuildDisplayText`, `MainWindow` scroll-to-end

**What:** Fine at 1500 ms. At 200 ms interval it is extra UI CPU for the whole meeting. Throttling in AUDIT-0002 covers this.

**Fix:** Same as AUDIT-0002 (throttle / append last line).

### AUDIT-0011 — Transcript epoch then AddSnapshot race

**Status:** DONE  
**Priority:** HIGH

**Where:** `Services/CaptureController.cs` `RecognizeCaptionsAsync` / `ClearTranscript`

**What:** Epoch is checked after OCR, then `AddSnapshot` runs unsynchronized. `ClearTranscript` can increment the epoch and clear the aggregator in that gap, so a late OCR result is appended to the new scribe. Same window can `NotifyParticipants` after `Reset()`.

**Fix:** One lock around epoch check + `AddSnapshot` (and participant notify). `ClearTranscript` must increment epoch and `Clear()` under that same lock.

### AUDIT-0012 — Save busy/disable starts after the save dialogs

**Status:** DONE  
**Priority:** HIGH

**Where:** `ViewModels/MainViewModel.cs` `SaveAsync` / `StopAndSaveAsync`

**What:** Busy-before-dialogs and Stop+Save-no-clear-if-restarted are in. Transcript is still read *after* dialogs (`GetTimedTranscript` / `GetTranscriptText`), so in-flight OCR during Save-while-capturing is included. The two reads are not atomic.

**Fix:** Snapshot timed + plain (one aggregator lock) at gate entry, off the UI thread; dialogs and write use that snapshot.

### AUDIT-0013 — Pause/Stop can still block the UI on autosave flush

**Status:** DONE  
**Priority:** HIGH

**Where:** `CaptureController.Stop`, `TranscriptAutoSaver.Stop` / `WaitAndDropTimer`

**What:** `Stop()` (used by Pause) waits up to 5 s for the autosave timer, then `Flush`es on the calling (UI) thread.

**Fix:** From the UI, only cancel capture. Join the autosave timer and flush on a worker (or async Stop). Keep pause non-blocking.

### AUDIT-0014 — JoinPending GetResult on the UI thread

**Status:** DONE  
**Priority:** HIGH

**Where:** `Services/CaptureController.cs` `ObserveLoop` / `Dispose`, `App.xaml.cs` `ReleaseStartupHandles`

**What:** Play-after-Pause joins via `ContinueWith` (not dispatcher `GetResult`). `Dispose` still `JoinPending`→`GetResult()` on the UI thread (`OnExit` / session ending). Cancelled `RecognizeAsync` can complete the loop task while WinRT still holds the pooled `SoftwareBitmap`.

**Fix:** Never `GetResult()` on the dispatcher (dispose on a worker). Await in-flight WinRT OCR before reusing the pool or completing the loop task. Invalidate queued `BeginCapture` on Dispose.

### AUDIT-0015 — Autosave Flush vs Clear race

**Status:** DONE  
**Priority:** HIGH

**Where:** `TranscriptAutoSaver.Flush` (releases `_gate` across `AppendAllLines`), `CaptureController.ClearTranscript`

**What:** Clear is allowed while capturing and does not stop the timer. Concurrent timer Flush vs `Flush(final: true)` + `Roll()` can duplicate lines in the old autosave file and/or lose the append cursor.

**Fix:** Serialize Flush+Roll (hold `_gate` for the whole append, or stop/join the timer around `ClearTranscript`). After a path/cursor change, drop in-flight writes to the old path.

### AUDIT-0016 — ParticipantImageWriter leaks GDI Regions

**Status:** DONE  
**Priority:** MED

**Where:** `Services/ParticipantImageWriter.cs` (~66)

**What:** `g.Clip` returns a `Region` that is never disposed; assigning `g.Clip = saved` leaks another. One leak per participant on each image write.

**Fix:** `g.SetClip(circle)` … `g.ResetClip()`; dispose any `Region` you materialize.

### AUDIT-0017 — GrabRawAsync swallows cancel then still captures

**Status:** DONE  
**Priority:** MED

**Where:** `Services/CaptureController.cs` `GrabRawAsync` (~283)

**What:** `OperationCanceledException` from the settle delay is swallowed, then `CopyFromScreen` and focus restore still run. After Pause this extra focus-switch/screenshot still happens.

**Fix:** Rethrow or return on cancel before capture; keep the restore `finally` only when activation succeeded and cancel did not fire.

### AUDIT-0018 — Remaining unnamed magic numbers

**Status:** DONE  
**Priority:** MED

**Where:** production `.cs` files (not Win32 API flags)

**What:** After naming the capture-sample constants, these still bury meaning in literals:

- `Core/Interop/Win32.cs` — process-name cache TTL `2000` ms (local `maxEntries = 64` is named)
- `Services/TranscriptAutoSaver.cs` — `WaitOne(TimeSpan.FromSeconds(5))`
- `Services/CaptureController.cs` — `Math.Max(1, AutoSaveIntervalMinutes)`; layout scale compare `1.0001`
- `Views/RegionHighlightForm.cs` — `Math.Max(500, durationMs)`
- `Services/ParticipantImageWriter.cs` — `width = 460`, `pad = 16`, `avatar = 44`, `rowGap = 12`
- `Services/ParticipantRoster.cs` — `HasAvatarContent` step `/ 24`; luma `0.114` / `0.587` / `0.299`
- `ViewModels/SettingsViewModel.cs` — validation ranges (`1..60` minutes, `0..5000` settle, `1..1000000` autosave prompt, upscale `1..4`) not shared with `AppSettings` min/max where those exist

**Fix:** Named constants (same style as `HasMeaningfulChange`). Prefer sharing settings bounds with `AppSettings.MinCaptureIntervalMs` / `MaxCaptureIntervalMs` where they already exist.

### AUDIT-0019 — Split CaptureController

**Status:** DONE  
**Priority:** MED

**Where:** `Services/CaptureController.cs`

**What:** `FocusAwareGrabber`, `CaptureFailurePolicy`, `CaptureSettings`, and `AutosaveScheduler` were extracted. The controller still owns loop lifecycle, OCR apply, Teams gating, transcript epoch, and diagnostics.

**Fix:** Keep extracting collaborators (loop runner, transcript apply) while `ICaptureController` stays the VM façade. No new NuGet.

### AUDIT-0020 — Split MainViewModel

**Status:** DONE  
**Priority:** MED

**Where:** `ViewModels/MainViewModel.cs`

**What:** `TranscriptDisplayText` holds the live/Show-All cap and overflow banner. Save/copy busy pipeline, settings toggles, and participant wiring are still in the VM.

**Fix:** Extract save/export (or a small save service) so the VM stays command routing + state.

### AUDIT-0021 — Split App startup shell

**Status:** DONE  
**Priority:** LOW

**Where:** `App.xaml.cs` (~329 lines)

**What:** Single-instance mutex/event, tray wiring, exception handlers, window-vs-region placement, and autosave-cache prompt are all in `App`.

**Fix:** Move single-instance and tray host to small types under `Core/Shell` (alongside `TrayIcon`). Keep `App` as composition root.

### AUDIT-0022 — Pause then Play can kill the new autosave timer

**Status:** DONE  
**Priority:** HIGH

**Where:** `Services/CaptureController.cs` `Stop` / `BeginCapture`, `Services/TranscriptAutoSaver.cs`

**What:** `Stop()` (Pause) queues `_autoSaver.Stop()` on the thread pool so the UI does not flush. `BeginCapture` then calls `_autoSaver.Start()` on the caller. If Play wins the race, Stop’s `WaitAndDropTimer` disposes the *new* timer and autosave stays dead for the rest of the session. `Start()` also sets `_running` and then starts autosave outside `_lifecycle`, so Pause in that window can cancel capture and still let `Start()` recreate the timer.

**Fix:** Serialize autosave Start/Stop/Dispose on one worker chain. Skip a queued Start when Stop/Dispose has moved the lifecycle epoch.

### AUDIT-0023 — Audio save/mix loads the whole meeting into RAM

**Status:** DONE  
**Priority:** HIGH

**Where:** `Services/AudioExport.cs` `TryReadPcm16` / `MixPcm16` / `TryMixWav`

**What:** Stop/Save used to read the full loopback WAV and mic WAV into `byte[]`, then allocate a third mix buffer. ~1 hour of 48 kHz stereo 16-bit is ~690 MB; loop + mic + mix ≈ 2 GB. A 3-hour call could OOM. `MixPcm16` also used to call `BitConverter.GetBytes` per sample.

**Fix:** Stream mix/encode (read/write in chunks). Write PCM samples with `BinaryPrimitives` into a span, no per-sample arrays. Do not hold three full-meeting buffers.

### AUDIT-0024 — PCM WAV header sizes are uint (4 GB cap)

**Status:** DONE  
**Priority:** HIGH

**Where:** `Services/PcmWavWriter.cs` `WriteHeaderUnlocked`, `Services/AudioAutoSave.cs` `TryRepairHeader`

**What:** RIFF `data`/`RIFF` sizes are `uint`. `_dataBytes` is `long` then truncated. Repair uses `(uint)(fileLength - 44)`, which also wraps. Stereo 48 kHz 16-bit overflows around ~6.5 hours. Hour-long meetings fit; all-day recordings become unplayable despite data on disk.

**Fix:** Cap recording with a clear status before 4 GB, or write RF64/WAVE64. Repair must not wrap. Two streams (loopback + mic) each have their own cap.

### AUDIT-0025 — WASAPI Read drops the tail of large packets

**Status:** DONE  
**Priority:** HIGH

**Where:** `Services/WasapiLoopbackCapture.cs` `Read`, `Services/LoopbackAudioSession.cs` `MixBufferBytes` (192 KB)

**What:** The WASAPI client buffer is 1 s. A float32 stereo 48 kHz packet can exceed 192 KB. `Read` copies a prefix, sets `frames` from the truncated byte count, then `ReleaseBuffer(got)` for the *whole* packet. The rest of the audio is discarded — glitches on busy devices.

**Fix:** Drain `GetBuffer` in a loop into a buffer ≥ one period (or ≥ mix `nBlockAlign * frames`), or enlarge the mix buffer to the initialized duration. Never `ReleaseBuffer` more frames than were copied unless the remainder is explicitly skipped and logged.

### AUDIT-0026 — PauseCore’s 2 s join can start a second audio pump

**Status:** DONE  
**Priority:** HIGH

**Where:** `Services/LoopbackAudioSession.cs` `PauseCore` / `StartOrResume`

**What:** Pause cancels the pump and `Wait`s only 2 s. If the pump is stuck in `Flush(true)` or WASAPI, Play starts another `Task.Run(Pump)` on the same `PcmWavWriter`. Two WASAPI clients and interleaved writes. Same class of bug as overlapping OCR loops.

**Fix:** Join the old pump to completion off the UI thread before Start (no 2 s cap, or cap then refuse Start and fail-soft). Epoch must make a late pump exit without writing.

### AUDIT-0027 — WAV header Flush(true) runs on the capture lock every 3 s

**Status:** DONE  
**Priority:** HIGH

**Where:** `Services/PcmWavWriter.cs` `Write` / `FlushHeaderUnlocked`, `Services/LoopbackAudioSession.cs` pump `_gate`

**What:** Every ~3 s the audio thread rewrites the 44-byte header and `Flush(flushToDisk: true)` while holding the writer lock, which the pump also holds around `Write`. Slow disks or OneDrive autosave folders stall the pump and drop WASAPI packets. OneDrive may also re-sync the growing file each flush.

**Fix:** Do not flush-to-disk on the capture thread. Rewrite the header in memory / `Flush(false)` on the pump; fsync on a worker or on Pause/Stop/Save snapshot. Keep crash-safety without blocking the packet loop.

### AUDIT-0028 — 15 s of quiet audio is treated as failure

**Status:** DONE  
**Priority:** HIGH

**Where:** `Services/LoopbackAudioSession.cs` `_silentAfterMs` (15 s), `Services/CaptureController.cs` `ApparentlySilent` → `AudioFailed` / `IsAudioCapturing`

**What:** Peak below ~0.02 used to set `ApparentlySilent` after 15 s. Status became `Audio unavailable` and the meter went idle while PCM was still written. Mute, a pause in speech, or Q&A could trip this. Exclusive-mode (never any audio) is the real target.

**Fix:** Only flag silence if peak never left 0 after start, with a three-minute window. Do not equate “quiet meeting” with capture failure. Mic session must not drive this (already true).

### AUDIT-0029 — Process loopback activation likely never sticks

**Status:** DONE  
**Priority:** HIGH

**Where:** `Services/WasapiLoopbackCapture.cs` `OpenProcessLoopback`

**What:** `ActivateAudioInterfaceAsync(..., out _)` drops the async operation before the callback. `handler.Client is IAudioClient` often fails because the RCW does not match the nested private COM interface (need QI by `IAudioClient` IID). Either failure falls back to device loopback, so NEW-0006 may never record Teams-only audio.

**Fix:** Keep `IActivateAudioInterfaceAsyncOperation` until `ActivateCompleted`. QI the result by the `IAudioClient` GUID (`Marshal.QueryInterface` / typed wrapper). Log when process activation fails vs when PID is 0.

### AUDIT-0030 — OpenMicrophone fallback can leak the first COM client

**Status:** DONE
**Priority:** MED

**Where:** `Services/WasapiLoopbackCapture.cs` `OpenMicrophone`

**What:** Communications-role open is tried first; on any exception the method opens Console without disposing a partially constructed enumerator/client/mix-format from the first attempt.

**Fix:** Dispose/release the first attempt in a `finally` (or a small `try` scope) before the Console fallback.

### AUDIT-0031 — Save transcodes a WAV that may still be open for write

**Status:** DONE
**Priority:** MED

**Where:** `ViewModels/TranscriptExportWorkflow.cs` `WriteAudioAsync`, `Services/PcmWavWriter.cs` `FileShare.Read`, `LoopbackAudioSession.SnapshotWavPath`

**What:** Save-while-capturing is allowed. Snapshot flushes the header, then dialogs run, then MF transcode/`File.Copy` reads the autosave WAV while the pump may still be appending (`FileShare.Read`). Transcode can fail or encode a truncated/inconsistent file. The file also grows during the dialogs.

**Fix:** After dialogs, flush the active writer on a worker and copy the current fixed-length PCM prefix in bounded chunks to a temporary WAV. Mix/transcode only the closed snapshot. Do not require Stop before Save.

### AUDIT-0032 — Autosave cache prompt counts files, not disk bytes

**Status:** DONE
**Priority:** MED

**Where:** `App.xaml.cs` startup cache prompt, `AudioAutoSave.CountFiles`

**What:** Threshold is a file count (default 500). Audio WAVs are hundreds of MB per hour; a handful of files can fill the disk before the prompt. 7-day WAV deletion helps but does not bound size during a long week of meetings.

**Fix:** Include total bytes (txt + wav) in the prompt and trigger it at a named 1 GiB budget as well as the configured file count. Keep the 7-day WAV sweep.

### AUDIT-0033 — Active audio autosave can exceed the disk budget

**Status:** OPEN
**Priority:** HIGH

**Where:** `Services/LoopbackAudioSession.cs`, `Services/PcmWavWriter.cs`, startup cache budget in `App.xaml.cs`

**What:** The 1 GiB autosave threshold is checked only at startup. A long meeting can cross the budget while Caption Scribe remains open and continue consuming disk space; the next prompt will not appear until restart.

**Fix:** Enforce the named autosave audio budget during recording using tracked byte counts, not a directory scan per packet. At the limit, stop audio writes cleanly and finalize playable WAV headers, surface/log the reason once, and keep captions running. Include existing matching autosave txt/WAV bytes when deciding whether audio can start; do not delete files automatically.
