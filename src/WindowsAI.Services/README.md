<div align="center">

# WindowsAI.Services

The Windows interop layer. Everything that touches Win32 or Windows-only APIs lives here,
so the core engine stays platform-agnostic.

![Target](https://img.shields.io/badge/net10.0--windows-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)
![Layer](https://img.shields.io/badge/Windows%20interop-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)

[![Home](https://img.shields.io/badge/%E2%86%90%20%20README-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)](../../README.md)
&nbsp;
[![Architecture](https://img.shields.io/badge/Architecture-0B0E14?style=for-the-badge&labelColor=0B0E14&color=B06BFF)](../../docs/ARCHITECTURE.md)

</div>

---

## Responsibility

This project isolates the platform. It references `WindowsAI.Core` and implements the abstractions
the core declares (most notably `ISecretStore`), plus all the native plumbing the overlay and the
shell rely on: global hotkeys, borderless window chrome, screen capture, the tray icon, and the
Windows Search retriever. Keeping this code in one place means the engine can be tested and reasoned
about without dragging in P/Invoke.

---

## Folder map

| Folder | Contents |
| :--- | :--- |
| `Hotkeys/` | `HotkeyManager` (multiplexes multiple global chords over one window subclass) and `HotkeyDefinition`. |
| `Interop/` | `NativeMethods` (P/Invoke surface) and `WindowChrome` (borderless windows, drag, resize, monitor placement). |
| `Capture/` | `ScreenCaptureService` — GDI grab of the monitor under the cursor into a frozen bitmap, then a cropped region. |
| `Tray/` | `NativeTrayIcon` — tray presence and menu. |
| `Security/` | `DpapiSecretStore` — per-user DPAPI encryption implementing the core's `ISecretStore`. |
| `Search/` | `WindowsSearchRetriever` (OLE DB over the live Windows Search index) plus the overlay launcher catalogs: `AppCatalog`, `FileNameSearch`, `SettingsCatalog`, and `OverlaySearchService`. |
| `Writing/` | `TextSelectionService` — synthesizes the copy/paste chords to read and replace the selection in any focused app. |
| `Inference/` | `OllamaLauncher` — detects and cold-starts a local Ollama server. |

---

## Key types

| Type | Role |
| :--- | :--- |
| `HotkeyManager` | Registers and dispatches global hotkeys through a single subclassed window; one manager serves all chords. |
| `WindowChrome` | Native window styling and positioning for the borderless overlay and floating tools. |
| `ScreenCaptureService` | Captures the monitor under the cursor up front, so the selection is drawn against a still image and never captures the dimming overlay. |
| `DpapiSecretStore` | Encrypts secrets per-user with `System.Security.Cryptography.ProtectedData`. |
| `WindowsSearchRetriever` | Queries `SYSTEMINDEX` over OLE DB (FREETEXT, user-profile scope, rank order) and snippets results. |
| `TextSelectionService` | Reads and replaces the focused selection via SendInput chords, with return-count checks, focus retry, and clipboard restore. |
| `NativeTrayIcon` | Owns the tray icon and context menu. |

---

## Implementation notes

- **SendInput sizing.** The input union must be large enough to hold a `MOUSEINPUT` (40 bytes on
  x64) or `SendInput` silently no-ops. `TextSelectionService` depends on this for its copy/paste
  chords.
- **Capture before dimming.** The screenshot is taken before the dim overlay is shown, so the
  selection spotlight is drawn over a frozen image and the overlay never appears in the crop.
- **One subclass, many chords.** `HotkeyManager` multiplexes `Alt+Space`, `Ctrl+Shift+Space`, and
  `Alt+Shift+S` over a single window subclass rather than registering separate windows.

---

## Dependencies

`System.Data.OleDb` (Windows Search), `System.Drawing.Common` (screen capture), and
`System.Security.Cryptography.ProtectedData` (DPAPI). References `WindowsAI.Core`.
