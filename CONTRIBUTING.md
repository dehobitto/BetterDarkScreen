# Contributing

Thanks for helping make BetterDarkScreen better.

## Ground rules

- **Stay light.** No new NuGet dependencies without a strong reason. Idle CPU should stay at ~0 % and RAM low. Create windows lazily and dispose them when they close.
- **Stay native.** UI follows Windows 11 (Fluent icons, theme and accent aware). Test light and dark mode.
- **Never leave a screen dark.** Any new dimming path must restore on exit, crash, display change and resume.

## Dev setup

```powershell
# .NET 8 SDK on Windows
dotnet run --project src
.\build.ps1          # release single-file exe in .\publish
```

## Pull requests

1. Fork and branch from `main`.
2. Keep PRs focused, one feature or fix each.
3. Describe what you tested: Windows build, monitors, GPU, HDR on or off, scaling.
4. Screenshots or GIFs help for UI changes.

## Bug reports

Please include:
- Windows version and build (`winver`)
- Monitor models, and whether each showed Hardware ✓/✗ and Gamma ✓/✗ (Settings → Displays)
- GPU and driver version, HDR on or off, display scaling
- Steps to reproduce, and your `%AppData%\BetterDarkScreen\settings.json` if relevant
