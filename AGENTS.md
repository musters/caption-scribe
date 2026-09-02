# Caption Scribe

Windows WPF/.NET 8 desktop app. Capture, OCR, and storage stay on-device. The app has no third-party runtime packages; xunit and related test packages are allowed in `CaptionScribe.Tests` only.

## Code review

When reviewing or changing this repo:

- Prefer high-confidence issues: correctness, races, leaks, GDI/bitmap lifetime, UI-thread blocking, Windows login/registry behavior, and performance.
- Do not add NuGet packages to the app project.
- Do not add any Third-Party of any kind to the app project without the users explicit consent.
- Do not add any unlicensed code to the app project.
- Capture loop: do not share pooled bitmaps across overlapping loops; cancel in-flight OCR on stop; join before Start/Dispose; pause must not block the UI.
- Late OCR after Clear/New Scribe must not reappear in the transcript (transcript epoch).
- Settings Save: create folders and apply startup registration before mutating `AppSettings`.
- After the project is built, always run the test suite.
- Keep Help and README in sync with user-facing behavior.
- Version lives in `CaptionScribe.csproj` (`<Version>`).
- Double check the code review suggestions, do a second pass, ensuring bad advice is not given and requirements do not conflict with one another.

## Testing

Run `dotnet test` from the repository root after building or changing code.

## Out of scope

Unless the user explicitly asks: no installer or MSIX packaging, no DXGI/Windows.Graphics.Capture rewrite, and no extra NuGet packages in the app project.
