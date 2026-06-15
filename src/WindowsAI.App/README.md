<div align="center">

# WindowsAI.App

The WinUI 3 presentation layer. The tray-resident overlay, the navigation shell, every page
and floating window, the view models, and the controllers that bridge hotkeys to UI.

![Target](https://img.shields.io/badge/WinUI%203%20%C2%B7%20net10.0--windows-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)
![Layer](https://img.shields.io/badge/Presentation-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)

[![Home](https://img.shields.io/badge/%E2%86%90%20%20README-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)](../../README.md)
&nbsp;
[![Architecture](https://img.shields.io/badge/Architecture-0B0E14?style=for-the-badge&labelColor=0B0E14&color=B06BFF)](../../docs/ARCHITECTURE.md)

</div>

---

## Responsibility

This is the application the user sees and the process that owns everything. It hosts the DI
container, starts to the tray, keeps the overlay alive, registers the global hotkeys, and renders
the Electric Iris design system. It references both `WindowsAI.Core` and `WindowsAI.Services` and
follows the MVVM pattern with `CommunityToolkit.Mvvm`.

The app starts with **no window shown**. The overlay is summoned on demand; the full shell opens
from the tray menu.

---

## Folder map

| Folder | Contents |
| :--- | :--- |
| `Windows/` | The floating surfaces: `OverlayWindow`, `WritingToolsWindow`, `RegionCaptureWindow`. |
| `ViewModels/` | MVVM view models, led by the singleton `ChatViewModel`; plus settings, history, chat-message, and tool-activity models. |
| `Services/` | App-level controllers and sync: `ThemeSyncService`, `WritingToolsController`, `VisualIntelligenceController`. |
| `Agents/` | `AgentToolFactory` (builds the tools per send) and `ToolActivity` (the surfaced step model). |
| `Controls/` | Custom controls: `RichMessageView` and `MessageContentControl` (markdown rendering), `SparkleIcon`, `FileGlyphs`, and `DialogService`. |
| `Helpers/` | `GlassBackdrop` and `TransparentBackdrop`, `ClipboardHelper`, `ShellIconProvider`. |
| `Converters/` | XAML value converters. |
| `Themes/` | `Tokens.xaml` and `Palette.xaml` — the Electric Iris design tokens. |
| `Assets/` | App icons, the Inter typeface, and the vendored WebView2 assets for rich message rendering. |

Top-level pages live in the project root: `MainWindow` (the shell), `MainPage` (Chat),
`HistoryPage`, `ModelsPage`, and `SettingsPage`.

---

## Key types

| Type | Role |
| :--- | :--- |
| `App` | Owns the DI container, crash logging, the always-alive overlay, the tray, and the global hotkeys. |
| `OverlayWindow` | The `Alt+Space` glass command surface: chat, app launching, and local search, with its own conversation. |
| `WritingToolsWindow` | The `Ctrl+Shift+Space` writing-tools popup over a text selection. |
| `RegionCaptureWindow` | The `Alt+Shift+S` screen-region capture and vision ask card. |
| `ChatViewModel` | Singleton chat state: messages, streaming with cancel/retry, attachments, model selection, RAG toggle. |
| `ThemeSyncService` | Mirrors the Windows light/dark and accent into the live brush resources; the runtime source of truth for brush values. |
| `WritingToolsController` / `VisualIntelligenceController` | Bridge the global hotkeys to their floating windows. |
| `AgentToolFactory` | Builds the agent tools per send and routes each call to a surfaced step line. |

---

## Design system

The visual language is **Electric Iris** — an indigo-violet accent (`#6E7BFF`) over layered glass,
set in the Inter typeface, with restrained motion. Tokens live in `Themes/`. The app follows the
Windows theme and accent by default but can pin its own look. Accessibility names are set on
icon-only controls across every surface for Narrator support.

---

## Build and run

```powershell
# Build (x64)
dotnet build WindowsAI.App.csproj -c Debug -p:Platform=x64

# Run with package identity (required — the bare .exe is not supported)
dotnet run --project WindowsAI.App.csproj
```

> The project targets WinUI 3 on the Windows App SDK. Launch through `dotnet run` so the app starts
> with package identity (AUMID); running the produced `.exe` directly fails to register.

---

## WinUI gotchas worth knowing

- **Window XAML and converters.** The standalone-compiled window XAML rejects `x:Bind` converters;
  those windows drive content from code-behind or function binds instead. A converter in window
  XAML can fail the build with no visible error text.
- **Custom backdrops.** `OnDefaultSystemBackdropConfigurationChanged` is overridden as a no-op to
  avoid a theme-change crash with the custom backdrop.
- **Round-button focus.** Round buttons set `UseSystemFocusVisuals="False"` and draw their own ring,
  because the default focus rectangle is square.

---

## Dependencies

`CommunityToolkit.Mvvm`, `CommunityToolkit.WinUI.Animations` and `.Media`, `Microsoft.Extensions.AI`,
`Microsoft.Extensions.Hosting`, and `Microsoft.WindowsAppSDK`. References `WindowsAI.Core` and
`WindowsAI.Services`.
