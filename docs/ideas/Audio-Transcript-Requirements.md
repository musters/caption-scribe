# Audio transcript requirements

Living requirements for **supplementary** audio capture and transcription. On-screen OCR remains the primary transcript. Audio does not replace it.

## Status

Accepted for scheduling. Split into **NEW-0001**–**NEW-0006** in `docs/BACKLOG.md`. Speech-to-text stays later (not those items).

## Product intent

Caption Scribe already builds a transcript from live captions via local OCR. Audio adds three capabilities:

1. **Second transcript** from speech-to-text, for comparison and correction against the OCR transcript.
2. **Speaker side** — microphone vs meeting playback (loopback) so local speech can be distinguished from other participants.
3. **Playback file** — save the meeting audio (and optionally the mic) for later listening, as `.mp3` if the in-box encoder allows it.

Everything stays on-device. No cloud speech services. No third-party runtime packages (same rule as the rest of the app: Microsoft / Windows / .NET only).

## Goals

- Capture **microphone** PCM while a scribe is active (user’s voice).
- Capture **meeting playback** PCM via Windows loopback (other participants, as heard on this PC). Prefer **process loopback** of the meeting app (e.g. Teams) over full-device loopback.
- Produce a **supplementary audio transcript** using in-box Windows speech APIs (`Windows.Media.SpeechRecognition` and/or SAPI), timestamped so it can be aligned with the OCR transcript.
- Use **channel origin** (mic vs loopback) as a speaker-side signal: mic ≈ local user, loopback ≈ remote participants. This is not full speaker diarization.
- Let the user **save audio** with the scribe (Stop/Save), default container **MP3** when Media Foundation can encode it; fall back to another in-box format (e.g. M4A/AAC or WAV) if MP3 is unavailable.
- Keep capture, STT, and file writes **local**. Microphone permission is explicit and off by default.
- Same capture session lifecycle as today: start/pause/stop, New Scribe, Clear, autosave folder, no extra NuGet.

## Non-goals

- Replacing or degrading the existing OCR caption pipeline.
- Injecting into Teams, Zoom, or any meeting client; no `SetWindowsHookEx` or other UI/audio “hooks.”
- Cloud STT (Azure Speech, etc.) and third-party engines (Whisper, NAudio, CSCore, etc.).
- True multi-speaker diarization beyond mic vs loopback.
- Capturing audio when Caption Scribe is idle (no active/paused scribe policy to be decided; default is only while capturing).
- Installer, MSIX, or DXGI work (unless separately requested).
- Perfect echo cancellation on the first iteration if Windows AEC is insufficient; document and iterate.

## Constraints

- **Stack:** .NET 8, `net8.0-windows10.0.19041.0` (Windows 10 2004+ / Windows 11), WinRT + WASAPI COM. No app `PackageReference` except what Windows already projects.
- **Process loopback** (`AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`) requires Windows 10 2004 or later — already the app’s floor.
- **Microphone:** app manifest microphone capability + runtime consent. If denied, mic path is off; loopback may still run.
- **Loopback** does not need microphone permission; it records what the machine is rendering (or one process). Disclose this in Help/Settings.
- **STT quality:** in-box recognizers are weaker than caption OCR for overlapping meeting speech. Treat audio text as secondary and optionally low-confidence.
- **MP3:** encode via Windows Media Foundation if the MP3 sink is present; do not ship LAME or other encoders.

## Capture design (requirements, not implementation)

### Sources

| Source | API direction | Content |
| --- | --- | --- |
| Microphone | WASAPI capture or WinRT `AudioGraph` / `MediaCapture` | Local user |
| Meeting audio | WASAPI **process loopback** on the meeting process; fallback to **device loopback** | Remote participants (and local playback) |

Do not use `SetWindowsHookEx`. Audio is WASAPI / MMDevice / AudioGraph only.

### Mixing and echo

- Keep mic and loopback as **separate streams** until save/STT so speaker-side is preserved.
- If both run, apply Windows echo cancellation / voice DSP when available so the user’s voice is not transcribed twice (once on mic, once on speakers).
- Device loopback may include notifications, music, and other apps. Process loopback should be the default when the meeting process can be identified (reuse existing Teams window/process detection where possible).

### Session

- Audio follows **Play / Pause / Stop** and tray equivalents.
- Pause: keep the **same file**. Stop writing PCM; on Play append **750 ms** of silence (named constant), not wall-clock pause. Optional later: a pause log for STT/OCR time mapping.
- Stop/Save: follow [Save flow](#save-flow); finalize audio with the transcript.
- New Scribe / Clear: start a new audio file; do not mix sessions.
- Late PCM (and later STT) after Clear/New Scribe must not attach to the new scribe (same epoch idea as OCR).

## Supplementary transcript

- OCR transcript remains what the main window shows by default.
- Audio STT is a **parallel timeline**: timestamp + text + source (`mic` | `loopback`).
- User-facing uses (later UX, not all required in v1):
  - Compare / merge hints when OCR and audio disagree.
  - Label lines as local vs remote using channel origin.
  - Optional view or export of the audio transcript (e.g. alongside `.txt`/`.md`).
- Alignment is by **time from session start**, not by forcing a single merged string in v1 unless comparison proves easy.

## UI

This fits the current shell: a **Settings menu** master toggle (same pattern as Capture Participants), status-bar feedback while capturing, and extra save prompts only when audio (or other sidecar files) exist.

### Enable: Settings ▸ Capture Audio

- Add a checkable **Settings ▸ Capture Audio** item (alongside Capture Participants). Off by default. Persisted in per-user settings like the other menu toggles.
- This is the **master switch**. When checked, Play starts audio with caption capture (mic and/or loopback per finer Settings, v1 can mean “meeting audio + mic if permitted”).
- Finer options (mic vs loopback, process vs device, STT) live in the Settings dialog, not a pile of extra menu items.
- Turning the toggle off mid-capture stops audio for the rest of the session; captions continue. Turning it on mid-capture starts audio from that point (no backfill).
- First mic use: Windows consent. If denied, leave loopback on if that source is enabled; do not fail caption capture.

### Status bar

- Keep the existing **Capture Status** line. When captions **and** audio are running, the message must say so, e.g. `Capturing captions and audio… Press Spacebar to Pause.`
- When audio is enabled but failed to start: `Capturing… Audio unavailable.` (wording TBD) — never look like recording if we are not.
- Pause: status matches today (Idle / paused) and does not claim audio is capturing.
- Add a **visual audio indicator** on the status bar (bottom): a compact **level / pulse animation** (bars or a meter) driven by loopback and/or mic peak, WPF-only, no extra packages.
- Animation is **live only while audio is actually capturing**. Idle, paused, or toggle-off: indicator hidden or grey and still.
- Do not place the meter where it could be OCR’d (main window already moves off the capture region).

### Save flow

If this scribe captured audio, Stop/Save **must** offer to save that audio **alongside** the transcript (same stem, same timestamp/title).

When the user is asked for the **meeting title** (existing save prompt), if there is more than one output (transcript + audio, and participants PNG when that feature also ran):

- Show a checkbox: **Save all files in a folder named after this meeting** (label TBD).
- Default: **checked** when audio (or multiple sidecars) exist.
- If checked: create a folder using the same name pattern as today’s file (`yyyy-MM-dd-Meeting-HH-mm-Title`) under the chosen save location; write transcript, audio, and any participants image into that folder with short names sharing the title stem.
- If unchecked: write files next to each other in the save folder, same stem, different extensions (`.txt`/`.md` + `.mp3`/fallback).
- If audio capture was on but produced nothing useful (zero-length / failed encode), skip audio and do not show the folder checkbox unless another sidecar exists.
- Default save folder and “ask each time” behave as they do for transcripts; the folder checkbox only groups files, it does not change where the parent location is.

## Audio file output

- **Record PCM** into the transcript autosave folder (`%APPDATA%\CaptionScribe\autosave` or the user’s autosave path), same stem as `scribe-yyyyMMdd-HHmmss.txt` (e.g. `scribe-….wav`, `scribe-…-mic.wav` if split).
- Keep the file open; append PCM; **flush to disk about every 3 s** and **rewrite WAV header sizes** so a crash leaves a playable partial file. On next launch, repair a leftover `.wav` whose header does not match file length. Standard RIFF WAVE, PCM 16-bit.
- Do not use `%TEMP%`.
- **Retention:** delete autosave `scribe-*.wav` (and mic siblings) older than **7 days** at startup, even if 30-day text cleanup is off. Do not delete user-exported MP3/WAV next to a saved transcript. Startup cache prompt counts WAV as well as txt.
- Stop/Save encodes that PCM → **MP3** via Windows Media Foundation when the MP3 sink exists; else M4A/AAC; else copy/keep WAV. No LAME or extra NuGet.
- Saving audio is implied when **Capture Audio** was on for this scribe and Stop/Save succeeds — not a second “save audio” toggle for v1.
- v1 save is **one mixed playback file** (loopback, mic mixed into both stereo channels, clip-safe, if that stream ran). Temp streams stay separate until encode.
- Do not upload.

## Settings and privacy (user-facing)

- **Settings ▸ Capture Audio** — master enable, off by default.
- Settings dialog (later / as needed): microphone, meeting loopback, prefer process loopback (Teams), Windows speech-to-text on audio.

Help and README must stay in sync: menu toggle, status + meter, save-into-folder checkbox, local only, what loopback records, mic permission, OCR remains primary.

## Settled (was open)

- Exclusive-mode / silent loopback: fail-soft; status **Audio unavailable** (or equivalent) when we are enabled but not writing or peaks stay ~0 — do not look like recording.
- Meter peak: **max of streams actually capturing** (loopback; mic too when NEW-0005 is on).

## Implementation plan

Order is how we should build it. Each step is a backlog **NEW** item. No extra NuGet; WASAPI / WinRT / Media Foundation only. OCR capture must not regress.

1. **NEW-0001** — Crash-safe PCM autosave writer (no WASAPI yet). Folder, stem, flush, header rewrite, startup repair, 7-day WAV cleanup. Unit-testable.
2. **NEW-0002** — Device loopback + session. Settings ▸ Capture Audio (off). Play/Pause/Stop, brief pause gap, Clear/New Scribe epoch, fail-soft (captions continue).
3. **NEW-0003** — Status line + level meter while audio is actually capturing.
4. **NEW-0004** — Stop/Save: encode PCM → MP3 (MF) with fallback; save-alongside + folder-grouping checkbox.
5. **NEW-0005** — Microphone as a second stream (consent, off by default; denial does not stop loopback or OCR).
6. **NEW-0006** — Prefer Teams **process** loopback; fall back to device loopback.

**Later (not NEW yet):** Windows speech-to-text, comparison UI, pause-time map for alignment, AEC polish.

## v1 vs later

**v1 (minimum useful):** NEW-0001–0004 (loopback recording you can save). NEW-0005–0006 complete the capture sources.

**Later:** STT, merge/compare, stronger speaker-side labeling, AEC, process-loopback for apps other than Teams.

## Notes

- Existing OCR path stays the source of truth for the live window unless the user chooses to view audio STT.
- Microsoft-only STT will not match caption OCR on typical Teams live captions; that is acceptable because audio is supplementary.
- Implementation must not add NuGet packages to the app project.
