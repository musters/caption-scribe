# Backlog

Living list of work not yet scheduled. IDs are stable; do not reuse.

- **NEW-####** — new product requirements
- **DEFER-####** — known issues we are postponing
- **AUDIT-####** — findings from code audits

**Status:** `OPEN` | `DONE`  
**Priority:** `LOW` | `MED` | `HIGH`

---

## NEW

_(none yet — product ideas such as audio live in `docs/ideas/`)_

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
