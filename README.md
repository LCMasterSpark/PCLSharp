# Plain Craft Launcher Sharp

⚠️ **这不是官方 Plain Craft Launcher，也不是龙腾猫跃（Hex-Dragon）的项目。该作者另有其人，这是我个人做的工作。**

![Version](https://img.shields.io/badge/version-v0.6pre-512BD4?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square)
![Platform](https://img.shields.io/badge/platform-Windows-0078D4?style=flat-square)
![UI](https://img.shields.io/badge/UI-WPF%20%2F%20MVVM-68217A?style=flat-square)

**Plain Craft Launcher Sharp（PCL Sharp / PCL#）** is a Windows Minecraft launcher by **LCMasterSpark**, reimplemented from scratch in C# (WPF / MVVM) to match the feature set of the original [Plain Craft Launcher 2](https://github.com/Hex-Dragon/PCL2).

> This is a **pre-release** (v0.6pre) — functionality is being actively completed and is not yet stable for daily use.

## What's New in v0.6pre

- Launch pipeline: Java discovery and selection, multi-provider login (Legacy / Microsoft / Yggdrasil / Authlib), argument generation, file completion, natives extraction, pre-run tasks, patches, custom commands, script export, and game window handling.
- Download system: vanilla Minecraft client downloads, Fabric/Quilt/Forge/NeoForge loader installation, modpack install and export, CurseForge and Modrinth resource search, and a unified download queue.
- Instance management: version scanning, rename/copy/delete, per-instance launch settings, local mod management with enable/disable/batch operations and update checks.
- Link (multiplayer) framework: Terracotta and EasyTier backend support, process start/stop, port allocation, connection status, and independent logging.
- Settings system: launch, download, link, UI, accessibility, and debug configuration, aligned with the original PCL semantics.
- Feature Hub: crash analysis (6 diagnostic patterns), update checking, account summary, skin summary, and an extension point catalog.
- Help system: embedded Help.zip with search and event routing.
- Architecture clean-up: zero external dependencies beyond CommunityToolkit.Mvvm, zero references to the original VB project.

## Highlights

- **MVVM architecture**: service layer split across 60+ interfaces and implementations, asynchronous launch pipeline with observable step progression, and UI decoupled via data binding.
- **Launch pipeline**: 15-step pipeline covering Java discovery → login → argument building → file completion → download management → natives extraction → pre-run → patches → custom commands → script export → GPU preference → memory optimization → process launch → window handling → crash diagnostics.
- **Login providers**: Legacy offline, Microsoft OAuth with device code flow, Yggdrasil for third-party authentication servers, and Authlib injector support.
- **Loader installation**: Fabric, Quilt, Forge, and NeoForge install chains with processor runners.
- **Resource search**: Unified search across CurseForge and Modrinth with facet filtering, pagination, and search history.
- **Mod management**: Local mod browsing with enable/disable, batch operations, update checking, and modpack export.
- **Link framework**: Terracotta and EasyTier backends with binary auto-detection, process lifecycle, port allocation, retry logic, and independent file logging.
- **Help system**: Built-in Help.zip with keyword search, category filtering, and action event routing (switch pages, join rooms, import modpacks, check updates).
- **Feature Hub**: Future feature center with crash analysis, update checking, account/skin summaries, and an extension point catalog.
- **Single-file publish target**: Self-contained Windows x64 release build for distribution.

## Module Status

| Module | Status | Description |
| --- | --- | --- |
| Launch pipeline | ~90% | Java discovery/selection → multi-provider login → argument building → file completion → natives → pre-run patches → process launch → window handling → crash diagnostics |
| Download system | ~85% | Vanilla client, Fabric/Quilt/Forge/NeoForge, modpack install/export, CurseForge & Modrinth search, download queue |
| Instance management | ~85% | Scan, rename, copy, delete, per-instance settings, local mod management, modpack export |
| Settings system | ~90% | Launch, download, link, UI, accessibility, debug, paths |
| Link (multiplayer) | ~50% | Terracotta & EasyTier backends, process lifecycle, port allocation — pending binary download and live status |
| More page | ~70% | Help system, diagnostics, Feature Hub (crash/update/account/skin/extension points) — pending feedback and treasure box |
| Crash analysis | ~40% | 6 diagnostic patterns (OOM, Java version, missing classes, Mixin, duplicate mods, permissions) — pending expansion to 20+ |
| Test coverage | 544 tests | Launch pipeline, download system, and discovery services covered end-to-end; VM-layer services pending |

## Build

Requirements:

- Windows
- .NET SDK 10

Build:

```bash
dotnet build PCLrmkBYCSharp.sln
```

Test:

```bash
dotnet test PCLrmkBYCSharp.sln
```

Run:

```bash
dotnet run --project PCLrmkBYCSharp
```

## Repository Layout

```text
PCLrmkBYCSharp/
  Models/                  Instance, version, download, login, link, settings, and help models
  Services/
    Launch/                Java discovery, login (Legacy/Ms/Yggdrasil), argument builder, file completer,
                           natives extractor, pre-run, patches, custom commands, process launcher, watcher
    Downloads/             Client download, community resource search (CurseForge/Modrinth),
                           loader install (Fabric/Quilt/Forge/NeoForge), modpack install/export, download manager
    Link/                  Terracotta and EasyTier backends, port allocator, process runner
    FeatureHub/            Feature module snapshots, crash analysis, account/skin summaries
    Loading/               Loading task infrastructure
    AppLoggerService.cs    Application logging
    AppPathService.cs      Path management
    AppSettingsService.cs  Settings persistence
  ViewModels/
    LaunchPageViewModel    Multi-file partials by responsibility
    InstancePageViewModel  Multi-file partials by responsibility
    DownloadPageViewModel  Multi-file partials by responsibility
    LinkPageViewModel      Link/multiplayer page state
    SetupPageViewModel     Settings page state
    OtherPageViewModel     Help, about, diagnostics, feature hub
    MainWindowViewModel    Shell layout and navigation
  Views/
    MainWindow.xaml        VS2022-style shell layout
    Pages/                 XAML pages for each module

PCLrmkBYCSharp.Tests/
  24 test files, 544 tests — xUnit
```

## Privacy

PCL Sharp does not send user data, login tokens, Minecraft credentials, or personal files to any third party. Login requests go directly to Microsoft OAuth endpoints or user-specified authentication servers. API requests to CurseForge and Modrinth are limited to resource searches. Diagnostic reports are purely local.

## Disclaimer

- PCL Sharp **is not** an official fork or replacement of the original Plain Craft Launcher.
- The original PCL trademarks, design, and behavioral references belong to Hex-Dragon (龙腾猫跃) and its contributors.
- This project is for learning and experimentation purposes only and should not replace the original PCL for daily use.
- The author assumes no responsibility for any issues arising from the use of this software.

## License

MIT License. See [LICENSE](LICENSE).

## Author

Made by **LCMasterSpark**.

### Acknowledgments

- **Hex-Dragon (龙腾猫跃)** — [Original PCL2](https://github.com/Hex-Dragon/PCL2)
- **bangbang93** — BMCLAPI mirror and Forge installation tools
- **MC百科** — Mod name and data index
- **EasyTier** — Link module reference
- All testers and contributors
