# Audio transcript requirements

Living requirements for **supplementary** audio capture and transcription. On-screen OCR remains the primary transcript. Audio does not replace it.

## Status

Idea. Not scheduled. No implementation until this document is accepted and scoped.

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
- Pause: stop appending STT and pause or stop writing audio (decide: pause file vs gap of silence).
- Stop/Save: follow [Save flow](#save-flow); finalize audio with the transcript.
- New Scribe / Clear: start a new audio file; do not mix sessions.
- Late audio STT after Clear/New Scribe must not attach to the new scribe (same epoch idea as OCR).

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

- Saving audio is implied when **Capture Audio** was on for this scribe and Stop/Save succeeds — not a second “save audio” toggle for v1.
- Format preference: **MP3**, then M4A/AAC, then WAV.
- Do not upload.

## Settings and privacy (user-facing)

- **Settings ▸ Capture Audio** — master enable, off by default.
- Settings dialog (later / as needed): microphone, meeting loopback, prefer process loopback (Teams), Windows speech-to-text on audio.

Help and README must stay in sync: menu toggle, status + meter, save-into-folder checkbox, local only, what loopback records, mic permission, OCR remains primary.

## Open questions

- Pause: keep the audio file open (silence) or segment files?
- One mixed MP3 vs separate mic and loopback files?
- If Teams process is missing, auto-fall back to device loopback or disable meeting audio?
- Show audio STT in the main transcript view, a side panel, or export-only in v1?
- Meter: mixed mic+loopback peak, or loopback-only?
- Language pack requirements for Windows speech vs OCR language packs.
- Exclusive-mode audio apps that never appear in loopback — show a status warning?
- Maximum recording length / disk budget before autosave folder prompts apply to `.mp3` as well.

## v1 vs later

**v1 (minimum useful):** Settings ▸ Capture Audio; loopback + optional mic while capturing; status text + level animation; save audio with transcript; folder-grouping checkbox on the title prompt; STT optional or export-only; no OCR replacement; Help/README.

**Later:** comparison UI, merge suggestions, stronger speaker-side labeling, AEC polish, process-loopback targeting for more than Teams.

## Notes

- Existing OCR path stays the source of truth for the live window unless the user chooses to view audio STT.
- Microsoft-only STT will not match caption OCR on typical Teams live captions; that is acceptable because audio is supplementary.
- Implementation must not add NuGet packages to the app project.
