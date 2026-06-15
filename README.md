<div align="center">

# WindowsAI

### A Windows-native, overlay-first AI assistant

Summon an assistant from anywhere with a keystroke. Chat with local or cloud models,
rewrite text in any app, and ask questions about whatever is on your screen — all
behind a single floating glass surface that lives in your system tray.

<br/>

![Platform](https://img.shields.io/badge/Platform-Windows%2011-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)
![Framework](https://img.shields.io/badge/WinUI%203-.NET%2010-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)
![Language](https://img.shields.io/badge/C%23-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)
![Privacy](https://img.shields.io/badge/Private%20by%20default-0B0E14?style=for-the-badge&labelColor=0B0E14&color=4DE3FF)

<br/>

[![Getting Started](https://img.shields.io/badge/%E2%96%B6%20%20Getting%20Started-6E7BFF?style=for-the-badge&labelColor=0B0E14)](#getting-started)
&nbsp;
[![Architecture](https://img.shields.io/badge/Architecture-0B0E14?style=for-the-badge&labelColor=0B0E14&color=B06BFF)](docs/ARCHITECTURE.md)
&nbsp;
[![Roadmap](https://img.shields.io/badge/Roadmap-0B0E14?style=for-the-badge&labelColor=0B0E14&color=B06BFF)](docs/ROADMAP.md)
&nbsp;
[![QA Checklist](https://img.shields.io/badge/QA%20Checklist-0B0E14?style=for-the-badge&labelColor=0B0E14&color=B06BFF)](docs/QA-checklist.md)

</div>

---

## Overview

WindowsAI brings an Apple-Intelligence-style assistant to Windows. It starts silently to
the system tray and is summoned with a global hotkey, surfacing a translucent overlay over
whatever you are doing. The same engine also powers a full multi-page desktop app for longer
conversations, history, model management, and settings.

The design language is **Electric Iris** — an indigo-violet accent (`#6E7BFF`) over layered
glass, set in the Inter typeface, with restrained motion. Everything is built to feel native
to Windows 11 while staying distinctly its own.

Three principles shape the project:

| Principle | What it means in practice |
| :--- | :--- |
| **Private by default** | Conversations, settings, and API keys stay on your PC. Nothing leaves the machine unless you deliberately use a cloud model. Secrets are encrypted with Windows DPAPI. |
| **Overlay-first** | The fastest path to the assistant is a keystroke, not a window. The tray-resident overlay is always alive and summons instantly. |
| **Local-capable** | A local Ollama model can power chat, retrieval, and writing tools with no network at all. Cloud providers are optional upgrades, not requirements. |

---

## Features

- **Instant overlay** — `Alt + Space` slides in a glass command surface for chat, app launching,
  and local file search, with its own independent conversation.
- **Full desktop app** — Chat, History, Models, and Settings behind a clean navigation shell.
- **Multi-provider chat** — Ollama (local), OpenRouter, Gemini (API key), and Gemini via Google
  sign-in (Vertex AI, OAuth). The active model is a single source of truth across every surface.
- **Local-first RAG** — opt-in "Search my PC" injects matching files from the Windows Search index
  as context, re-ranked by local embeddings, with source chips under each reply.
- **Agent tools** — the assistant can search your PC, open files, fetch the current date/time, and
  run web searches; each tool call surfaces as a compact step above the answer.
- **Type-anywhere writing tools** — `Ctrl + Shift + Space` over selected text opens proofread,
  rewrite, summarize, and other transforms that paste back into any app.
- **Visual intelligence** — `Alt + Shift + S` captures a screen region and asks a vision model
  about it.
- **Theming** — follows the Windows light/dark mode and accent, or pins its own Electric Iris look.

### Keyboard shortcuts

| Shortcut | Action |
| :--- | :--- |
| `Alt + Space` | Summon or dismiss the overlay |
| `Ctrl + Shift + Space` | Open writing tools over the current text selection |
| `Alt + Shift + S` | Capture a screen region and ask about it |
| `Esc` | Dismiss the overlay or any floating surface |

---

## Architecture at a glance

WindowsAI is a layered solution: a platform-agnostic core, a Windows interop layer, and the
WinUI presentation layer on top. Dependencies point inward only.

```
                       +---------------------------+
                       |       WindowsAI.App       |   WinUI 3 presentation
                       |  overlay - shell - pages  |   views, view models, controllers
                       +-------------+-------------+
                                     |
                +--------------------+--------------------+
                |                                         |
   +------------v-----------+               +-------------v------------+
   |   WindowsAI.Services    |  ----------->  |     WindowsAI.Core       |
   |  Win32 interop, hotkeys |               |  chat, providers, history |
   |  capture, tray, DPAPI   |               |  settings, RAG, writing   |
   +------------------------+               +--------------------------+
```

- **WindowsAI.Core** — chat orchestration, model providers, conversation history, settings,
  retrieval, writing transforms, and the secret-store abstraction. No Windows UI dependencies.
- **WindowsAI.Services** — Windows-specific interop: global hotkeys, window chrome, screen capture,
  tray icon, DPAPI-backed secret storage, and the Windows Search retriever.
- **WindowsAI.App** — the WinUI 3 application: the overlay, the navigation shell, all pages and
  windows, view models, and the controllers that bridge hotkeys to UI.

For the full design — DI lifetimes, the streaming chat pipeline, the provider model, and the
runtime data layout — see the architecture guide.

[![Read the Architecture Guide](https://img.shields.io/badge/Read%20the%20Architecture%20Guide-6E7BFF?style=for-the-badge&labelColor=0B0E14)](docs/ARCHITECTURE.md)

---

## Project map

Every project carries its own README describing its responsibilities, key types, and how it fits
the whole.

| Project | Role | Documentation |
| :--- | :--- | :---: |
| `src/WindowsAI.Core` | Platform-agnostic engine: chat, providers, history, settings, RAG, writing | [![Docs](https://img.shields.io/badge/Open-6E7BFF?style=flat-square&labelColor=0B0E14)](src/WindowsAI.Core/README.md) |
| `src/WindowsAI.Services` | Windows interop: hotkeys, capture, tray, DPAPI, Windows Search | [![Docs](https://img.shields.io/badge/Open-6E7BFF?style=flat-square&labelColor=0B0E14)](src/WindowsAI.Services/README.md) |
| `src/WindowsAI.App` | WinUI 3 presentation: overlay, shell, pages, view models | [![Docs](https://img.shields.io/badge/Open-6E7BFF?style=flat-square&labelColor=0B0E14)](src/WindowsAI.App/README.md) |
| `tests/WindowsAI.Core.Tests` | xUnit unit tests over Core | [![Docs](https://img.shields.io/badge/Open-6E7BFF?style=flat-square&labelColor=0B0E14)](tests/WindowsAI.Core.Tests/README.md) |
| `tools/WindowsAI.Smoke` | Headless smoke runner over the Core paths the UI depends on | [![Docs](https://img.shields.io/badge/Open-6E7BFF?style=flat-square&labelColor=0B0E14)](tools/WindowsAI.Smoke/README.md) |

---

## Getting started

> For a full walkthrough — detailed prerequisites, step-by-step setup for every provider, and
> troubleshooting — see the [Setup Guide](docs/SETUP.md). The quick-start below covers the essentials.

[![Full Setup Guide](https://img.shields.io/badge/%E2%96%B6%20%20Full%20Setup%20Guide-6E7BFF?style=for-the-badge&labelColor=0B0E14)](docs/SETUP.md)

### Prerequisites

| Requirement | Notes |
| :--- | :--- |
| Windows 11 | Mica and the rounded-glass chrome target Windows 11. Windows 10 (1809+) builds and runs with reduced backdrop fidelity. |
| .NET 10 SDK | The solution targets `net10.0` and `net10.0-windows10.0.26100.0`. |
| Windows App SDK 1.8 | Restored automatically via NuGet on first build. |
| Ollama (optional) | For fully local chat, retrieval, and writing tools. Pull a model, for example `ollama pull gemma3`. |

### Build

```powershell
git clone https://github.com/DrVanHelsing/WindowsAI.git
cd WindowsAI
dotnet build src\WindowsAI.App\WindowsAI.App.csproj -c Debug -p:Platform=x64
```

### Run

Launch the app through the .NET CLI so it starts with package identity. Running the bare `.exe`
directly is not supported.

```powershell
dotnet run --project src\WindowsAI.App\WindowsAI.App.csproj
```

The app starts to the tray. Press `Alt + Space` to summon the overlay.

### Configure models

Open **Settings** (from the tray menu or the app shell) to:

- point the local provider at your Ollama endpoint,
- paste an OpenRouter or Gemini API key (stored encrypted with DPAPI),
- sign in with Google for Gemini via Vertex AI,
- add a Tavily API key for web search.

No keys are required for local-only use.

---

## Testing

```powershell
# Unit tests (xUnit) over Core
dotnet test tests\WindowsAI.Core.Tests

# Headless smoke runner over the Core paths the UI depends on
dotnet run --project tools\WindowsAI.Smoke
```

The smoke runner exercises settings round-trips, conversation persistence, active-model sync,
Windows Search retrieval with embedding re-rank, model catalog discovery, live local streaming,
and the agent tool-calling loop. See the [smoke runner docs](tools/WindowsAI.Smoke/README.md) for
prerequisites.

---

## Privacy and data

WindowsAI stores everything under your local profile and nothing on a server:

| Data | Location | Protection |
| :--- | :--- | :--- |
| Conversations | `%LOCALAPPDATA%\WindowsAI\conversations\` | One JSON file per conversation, on-disk only |
| Settings | `%LOCALAPPDATA%\WindowsAI\` | Plain JSON, on-disk only |
| API keys and tokens | `%LOCALAPPDATA%\WindowsAI\secrets\` | Encrypted with Windows DPAPI (per-user) |

Network calls happen only when you actively select a cloud model or enable web search. Local
models keep the entire flow offline.

---

## Documentation

| Document | Contents |
| :--- | :--- |
| [Setup guide](docs/SETUP.md) | Full prerequisites, per-provider setup walkthroughs, and troubleshooting |
| [Architecture](docs/ARCHITECTURE.md) | Layers, DI, the chat pipeline, the provider model, runtime data layout |
| [Roadmap and status](docs/ROADMAP.md) | Phase-by-phase status of every feature |
| [QA checklist](docs/QA-checklist.md) | Manual sign-offs that need a human at the keyboard |
| Per-project READMEs | Linked in the [project map](#project-map) above |

---

<div align="center">

Built with WinUI 3 and .NET 10 · Designed in Electric Iris

</div>
