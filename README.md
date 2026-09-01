# Caption Scribe

Caption Scribe captures live captions from meetings and conversations using on-screen OCR and stitches
them into a running, timestamped transcript. Everything runs locally on your PC — no cloud services, no
meeting bot, no accounts.

It was built primarily for Microsoft Teams live captions, but it works with any application whose captions
render on screen.

## Features

- **Live caption capture** — point it at a screen region and it OCRs the captions on an interval,
  de-duplicating and stitching the scrolling text into one continuous transcript.
- **Timestamps** — optional, either per line or only at the start of each speaker's turn.
- **Autosave** — periodically appends finalized lines to a rolling file, so a crash loses very little.
- **Save & clean up** — export to `.txt`/`.md`, with an optional cleanup pass that fixes common OCR
  mistakes (e.g. `f`→`t` misreads) and collapses repeated speaker names.
- **Region tools** — drag-select a capture region on any monitor, highlight it, and re-select quickly.
- **System tray** — runs minimized to the tray; start/stop from the tray or the main window.
- **Participants image (experimental)** — optionally builds a PNG of meeting participants (avatar + name)
  from the captured region. Off by default; enable it under **Settings**.
- **No cloud dependencies** — capture, OCR, and storage all happen on this PC.

## Requirements

- Windows 10, build 19041 (2004) or later, Windows 11
- [.NET 8 SDK]
   - Install via winget (quiet, command-line): `winget install --id Microsoft.DotNet.SDK.8 -e`
     Or download and run installer from: https://dotnet.microsoft.com/download/dotnet/8.0
- A Windows OCR language pack (**Settings ▸ Time & language ▸ Language & region**). Most English installs
  already include one.

## Build & run

```powershell
# from the repository root
dotnet build
dotnet run --project CaptionScribe.csproj
```

Or open `CaptionScribe.sln` in Visual Studio 2022 and press **F5**.

## Usage

1. In your meeting app (e.g. Teams), turn on **live captions**.
2. In Caption Scribe, pick a capture region — the **Select** toolbar button, **Settings ▸ Select Capture
   Region**, or click the region readout at the top — then drag a box over the caption area (any monitor).
3. Start capturing: the **Play** button, **Space**, the centre prompt, or **File ▸ Active**. The transcript
   fills in live.
4. Use **Save** (or **Stop**) to write a file; you'll be offered a cleanup pass and asked for a meeting title.

**Tips for accuracy:** increase the caption font size in the source app, draw the region snugly around just
the caption lines, and raise the **Upscale factor** (Settings) if lines are being missed.

## Settings

Defaults live in [`appsettings.json`](appsettings.json); your changes are saved per-user under
`%APPDATA%\CaptionScribe\settings.json`. Notable settings include run on startup (tray at Windows sign-in),
the capture interval, upscale factor, OCR enhancement, timestamps, autosave interval/folder, default save
folder, and the experimental participants capture.

## Tests

```powershell
dotnet test
```

The suite has Unit Tests covering transcript aggregation/formatting/cleanup, settings, view-models, OCR
pixel conversion, frame-buffer pooling, and participant collection.

## Project layout

- `Core/` — cross-cutting infrastructure: `Mvvm`, `Logging`, `Interop` (Win32), `Shell` (tray).
- `Models/` — settings and transcript data types.
- `Services/` — capture, OCR, transcript aggregation/cleanup, autosave, and participants.
- `ViewModels/` — MVVM view-models.
- `Views/` — WPF windows and WinForms overlays.
- `CaptionScribe.Tests/` — xUnit tests.

## Privacy

Caption Scribe reads pixels from a screen region you choose and runs OCR locally. Transcripts are written
only to folders you pick (or the per-user autosave folder). Nothing is sent off the machine to the cloud.

## License

[MIT](LICENSE) © 2026 Musters Consulting
