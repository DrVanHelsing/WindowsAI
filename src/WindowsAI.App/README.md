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
| `Windows/` | The floating surfaces: `OverlayWindow`, `WritingToolsWindow`, `RegionCaptureWindow`, and the first-run `OnboardingWindow` setup wizard. |
| `ViewModels/` | MVVM view models, led by the singleton `ChatViewModel`; plus `SettingsViewModel` (feature-organized), `ProvidersViewModel` (the connection list), `PcIndexingViewModel`, history, chat-message, and tool-activity models. |
| `Services/` | App-level controllers and sync: `ThemeSyncService`, `WritingToolsController`, `VisualIntelligenceController`. |
| `Agents/` | `AgentToolFactory` (builds the tools per send) and `ToolActivity` (the surfaced step model). |
| `Controls/` | Custom controls: `RichMessageView` and `MessageContentControl` (markdown rendering), `SparkleIcon`, `FileGlyphs`, `DialogService`, and `ProviderEditDialog` (the add/edit-provider dialog with Test + Fetch models). |
| `Helpers/` | `GlassBackdrop` and `TransparentBackdrop`, `ClipboardHelper`, `ShellIconProvider`. |
| `Converters/` | XAML value converters. |
| `Themes/` | `Tokens.xaml` and `Palette.xaml` — the Electric Iris design tokens. |
| `Assets/` | App icons, the Inter typeface, and the vendored WebView2 assets for rich message rendering. |

Top-level pages live in the project root: `MainWindow` (the shell), `MainPage` (Chat),
`HistoryPage`, `ModelsPage` (the **Models & providers** hub — provider cards + Add/Edit/Test/Remove
plus the model picker), and `SettingsPage` (organized **by feature**: Appearance, Chat, Visual
Intelligence, Writing Tools, Web Search, Image Generation, PC Indexing, Privacy, About).

---

## Key types

| Type | Role |
| :--- | :--- |
| `App` | Owns the DI container, crash logging, the always-alive overlay, the tray, and the global hotkeys. |
| `OverlayWindow` | The `Alt+Space` glass command surface: chat, app launching, and local search, with its own conversation. |
| `WritingToolsWindow` | The `Ctrl+Shift+Space` writing-tools popup over a text selection. |
| `RegionCaptureWindow` | The `Alt+Shift+S` screen-region capture and vision ask card. |
| `ChatViewModel` | Singleton chat state: messages, streaming with cancel/retry, attachments, model selection, RAG toggle. |
| `OnboardingWindow` | First-run setup wizard: connect a provider, choose features, set up PC indexing, personalize. Gated by `Onboarding.HasCompleted`. |
| `ProvidersViewModel` / `ProviderEditDialog` | Manage the data-driven provider connections (add/edit/test/remove/enable, fetch models, Google sign-in). |
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
- **`{StaticResource}` on a Window's root element.** This *also* silently fails the markup compiler
  (MSB3073, no error text). Set the root `Background`/`FontFamily` in code-behind instead
  (`OnboardingWindow` does this). The same silent failure masks ordinary C# errors in a Window's
  code-behind — when a Window build fails with no message, reduce the `.xaml` to minimal markup to
  surface the real compiler error.
- **Custom backdrops.** `OnDefaultSystemBackdropConfigurationChanged` is overridden as a no-op to
  avoid a theme-change crash with the custom backdrop.
- **Round-button focus.** Round buttons set `UseSystemFocusVisuals="False"` and draw their own ring,
  because the default focus rectangle is square.

---

## Dependencies

`CommunityToolkit.Mvvm`, `CommunityToolkit.WinUI.Animations` and `.Media`, `Microsoft.Extensions.AI`,
`Microsoft.Extensions.Hosting`, and `Microsoft.WindowsAppSDK`. References `WindowsAI.Core` and
`WindowsAI.Services`.
