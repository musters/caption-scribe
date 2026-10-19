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

**Status:** OPEN  
**Priority:** HIGH

**Where:** `Services/CaptureController.cs` (~214, 252), `ViewModels/MainViewModel.cs` (~156)

**What:** After consecutive capture failures the loop sets `_running = false` in `finally` and then raises `CaptureStateChanged`. Until the UI `Refresh`/`SetActive` runs, `IsCapturing` is already false. A Pause click in that window sees idle and calls `Start()`, so the user restarts capture while trying to pause.

**Fix later:** Leave `_running` true until `AutoPause`/`Stop()` runs, then fire `CaptureStateChanged`. Make Pause always `Stop()` (idempotent). Do not treat “loop already exiting” as Play.

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

**Where:** `Services/ScreenCaptureService.cs` (`HasMeaningfulChange`, step 4, threshold `n/200`)

**What:** Compare is native color (green samples), not the enhanced OCR image. Video, carets, or anti-aliased edges easily exceed 0.5% of samples, so upscale+OCR runs every interval even when caption *text* is idle.

**Fix:** Raise threshold and/or compare after grayscale; ignore low-contrast pixels; skip OCR when the last recognized line list is unchanged.

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

**Status:** OPEN  
**Priority:** LOW

**Where:** `Services/TranscriptAutoSaver.cs` `Flush`, `TranscriptAggregator.GetLines`

**What:** Every minute: `ToList()` of all n strings, then append `n - 20`. Cheap at 10k lines. Two overlapping flushes (slow OneDrive path) can duplicate a slice.

**Fix:** `GetLines(start, count)` without copying the prefix; single-flight around IO.

### AUDIT-0010 — Default 200-line TextBox is still a full document swap

**Status:** OPEN  
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

**Where:** `ViewModels/MainViewModel.cs` `SaveAsync` / `StopAndSaveAsync`, tray in `App.xaml.cs` / `TrayIcon.cs`

**What:** `_saveGate` is taken immediately, but `SetBusy` waits until after cleanup/title/file dialogs. Nested dialogs pump messages, so tray Play/Pause/Stop/New Scribe/Exit still run. Pause/Stop during Save can stop capture; Play during Stop+Save then a successful save can `ClearTranscript()` on the restarted session.

**Fix:** Set busy/reentrancy (and tray `SetBusy`) before any dialog. Snapshot the timed transcript at gate entry. `StopAndSave` must not clear if capture was restarted. Tray Exit should use the same gate.

### AUDIT-0013 — Pause/Stop can still block the UI on autosave flush

**Status:** DONE  
**Priority:** HIGH

**Where:** `CaptureController.Stop`, `TranscriptAutoSaver.Stop` / `WaitAndDropTimer`

**What:** `Stop()` (used by Pause) waits up to 5 s for the autosave timer, then `Flush`es on the calling (UI) thread.

**Fix:** From the UI, only cancel capture. Join the autosave timer and flush on a worker (or async Stop). Keep pause non-blocking.

### AUDIT-0014 — JoinPending GetResult on the UI thread

**Status:** DONE  
**Priority:** HIGH

**Where:** `Services/CaptureController.cs` `JoinPending` (Play-after-Pause, Dispose)

**What:** `loop.GetAwaiter().GetResult()` on the dispatcher. WinRT `RecognizeAsync` often ignores `Cancel()` until it finishes, so Play-after-Pause and shutdown can freeze the UI for seconds. The join itself is required so pooled bitmaps are not shared.

**Fix:** Join before Start/Dispose, but await the old loop off the dispatcher (`async` Start/Dispose, or join on a worker then continue Start). Do not `GetResult()` on the UI thread.

### AUDIT-0015 — Autosave Flush vs Clear race

**Status:** DONE  
**Priority:** HIGH

**Where:** `TranscriptAutoSaver.Flush` (releases `_gate` across `AppendAllLines`), `CaptureController.ClearTranscript`

**What:** Clear is allowed while capturing and does not stop the timer. Concurrent timer Flush vs `Flush(final: true)` + `Roll()` can duplicate lines in the old autosave file and/or lose the append cursor.

**Fix:** Serialize Flush+Roll (hold `_gate` for the whole append, or stop/join the timer around `ClearTranscript`). After a path/cursor change, drop in-flight writes to the old path.

### AUDIT-0016 — ParticipantImageWriter leaks GDI Regions

**Status:** OPEN  
**Priority:** MED

**Where:** `Services/ParticipantImageWriter.cs` (~66)

**What:** `g.Clip` returns a `Region` that is never disposed; assigning `g.Clip = saved` leaks another. One leak per participant on each image write.

**Fix:** `g.SetClip(circle)` … `g.ResetClip()`; dispose any `Region` you materialize.

### AUDIT-0017 — GrabRawAsync swallows cancel then still captures

**Status:** OPEN  
**Priority:** MED

**Where:** `Services/CaptureController.cs` `GrabRawAsync` (~283)

**What:** `OperationCanceledException` from the settle delay is swallowed, then `CopyFromScreen` and focus restore still run. After Pause this extra focus-switch/screenshot still happens.

**Fix:** Rethrow or return on cancel before capture; keep the restore `finally` only when activation succeeded and cancel did not fire.
