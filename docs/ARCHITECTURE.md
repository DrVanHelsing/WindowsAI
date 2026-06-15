<div align="center">

# Architecture

How WindowsAI is put together: the layers, the dependency graph, the chat pipeline,
the provider model, and where data lives at runtime.

[![Home](https://img.shields.io/badge/%E2%86%90%20%20README-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)](../README.md)
&nbsp;
[![Roadmap](https://img.shields.io/badge/Roadmap-0B0E14?style=for-the-badge&labelColor=0B0E14&color=B06BFF)](ROADMAP.md)

</div>

---

## Layered design

The solution is three layers plus tests and tooling. Dependencies flow strictly inward:
the UI knows the services and the core; the services know the core; the core knows nothing
about Windows UI. This keeps the engine testable in isolation and the platform code thin.

```
   WindowsAI.App          WinUI 3 presentation
        |                  views, view models, controllers, theming
        |  depends on
        v
   WindowsAI.Services      Windows interop
        |                  hotkeys, capture, tray, DPAPI, Windows Search
        |  depends on
        v
   WindowsAI.Core          platform-agnostic engine
                           chat, providers, history, settings, RAG, writing
```

| Layer | Target framework | May reference |
| :--- | :--- | :--- |
| `WindowsAI.Core` | `net10.0` | NuGet only — no Windows UI |
| `WindowsAI.Services` | `net10.0-windows10.0.26100.0` | Core |
| `WindowsAI.App` | `net10.0-windows10.0.26100.0` (WinUI 3) | Core, Services |

---

## Composition and dependency injection

The application owns a single `Microsoft.Extensions.DependencyInjection` container, built once at
startup in `App.ConfigureServices` and exposed as `App.Services`.

- `AddWindowsAiCore()` (in `WindowsAI.Core`) registers the engine: settings and conversation stores,
  the chat providers, the model catalog, the active-model service, the chat orchestrator, and the
  writing, vision, image, and web-search services.
- The app then layers in the Windows implementations the core declares as abstractions — most
  importantly `ISecretStore`, implemented by `DpapiSecretStore`, and `ILocalContextRetriever`,
  composed from the Windows Search retriever wrapped in the embedding re-ranker.

### Notable lifetimes

| Service | Lifetime | Why |
| :--- | :--- | :--- |
| `ChatViewModel` | Singleton | The active model and the shell conversation must stay in sync across the Chat page, the Models page, and the overlay. |
| `ActiveModelService` | Singleton | Single source of truth for the selected model; raises a `Changed` event so every surface updates live. |
| `SettingsViewModel`, `HistoryViewModel` | Transient | Page-scoped state; recreated per navigation. |
| Providers, stores, orchestrator | Singleton | Stateless or cache-backed; shared safely. |

### The independent overlay conversation

The overlay is meant to feel like a separate scratchpad, so it does not share the shell's
`ChatViewModel`. Instead, `App.CreateChatViewModel` resolves a **fresh** `ChatViewModel` with the
same DI-provided dependencies. Both instances persist into the same history store, so the overlay
and the shell write to one shared conversation archive while keeping independent live state.

---

## The chat pipeline

All chat flows through `ChatOrchestrator` in the core. It resolves the active provider and model
from settings and streams the reply token by token.

```
 ChatViewModel
     |  builds the message list (system + history + user, plus any RAG context)
     v
 ChatOrchestrator.StreamReplyAsync(messages, tools?)
     |
     +-- no tools --> active provider's IChatClient --> stream text deltas
     |
     +-- with tools -> FunctionInvokingChatClient
                          model -> tool call -> tool result -> model -> ...
                          (yields only assistant text; tool steps surface separately)
```

Two design points matter:

1. **Streaming throughout.** `StreamReplyAsync` returns an `IAsyncEnumerable<string>` of text
   deltas. The UI renders them as they arrive and can cancel mid-stream via a `CancellationToken`,
   keeping the partial reply.
2. **Graceful tool fallback.** Some local models (such as Gemma via Ollama) reject requests that
   carry tools. That rejection arrives before any token streams, so the orchestrator catches it on
   the first move and retries once without tools, rather than failing the message.

---

## The provider model

Every backend implements `IChatProvider` (id, display name, an `IChatClient` factory, and a vision
capability flag). Providers are registered as a collection and selected by id from settings.

| Provider | Id | Backend |
| :--- | :--- | :--- |
| `OllamaChatProvider` | `ollama` | Local Ollama server (default `http://localhost:11434`) |
| `OpenRouterChatProvider` | `openrouter` | OpenRouter cloud, OpenAI-compatible |
| `GeminiChatProvider` | `gemini` | Google Gemini via an AI Studio API key |
| `VertexChatProvider` | `vertex` | Gemini via Vertex AI, authenticated with a Google sign-in (OAuth) |

`OpenAiCompatibleChatProvider` is the shared base for the OpenAI-shaped backends. `ModelCatalog`
discovers available models (local pulls plus the configured cloud catalogs), and `ActiveModelService`
holds the current selection and broadcasts changes.

Vision requests are special: `VisionService` always needs a multimodal backend even when the
everyday chat model is local text-only. It prefers the active provider when multimodal, otherwise
the first configured vision provider in priority order Gemini, then Vertex, then OpenRouter.

---

## Retrieval (local-first RAG)

When the "Search my PC" toggle is on, the app injects matching local files as transient context:

```
 query
   |
   v
 WindowsSearchRetriever     keyword recall over the live Windows Search index (OLE DB, FREETEXT)
   |  over-fetches a candidate pool
   v
 RerankingContextRetriever  re-orders by cosine similarity of local nomic-embed-text embeddings
   |  (falls back to keyword order if embeddings are unavailable)
   v
 top documents -> transient system message -> source chips under the reply
```

The retriever is composed in the app: `ILocalContextRetriever` is built from the platform
`WindowsSearchRetriever` wrapped by the core's `RerankingContextRetriever` and `IEmbeddingClient`.

---

## Agent tools

When tools are enabled, the orchestrator runs the tool-calling loop and the app surfaces each call
as a compact step line above the reply. Tools are built per send by `AgentToolFactory`, so the
shell and overlay update independently.

| Tool | Purpose | Notes |
| :--- | :--- | :--- |
| `search_my_pc` | Runs the Windows Search RAG | Drives the "Searching your PC" indicator and source chips |
| `open_path` | Opens a file or folder | Constrained to paths a prior `search_my_pc` actually surfaced this conversation |
| `get_current_datetime` | Returns the local date and time | Always offered |
| `search_web` | Google Programmable Search | Requires a configured engine id and API key; results render as link chips |

---

## Windows interop

`WindowsAI.Services` isolates everything that touches Win32 or Windows-only APIs:

- **Hotkeys** — `HotkeyManager` multiplexes multiple global chords (`Alt+Space`,
  `Ctrl+Shift+Space`, `Alt+Shift+S`) over one window subclass.
- **Window chrome** — `WindowChrome` and `NativeMethods` handle borderless windows, drag, resize,
  and monitor placement.
- **Capture** — `ScreenCaptureService` grabs the monitor under the cursor via GDI into a frozen
  bitmap, then crops the user's selection.
- **Tray** — `NativeTrayIcon` provides the tray presence and menu.
- **Secrets** — `DpapiSecretStore` encrypts keys and tokens per-user with Windows DPAPI.
- **Search** — `WindowsSearchRetriever`, `AppCatalog`, `FileNameSearch`, and `SettingsCatalog`
  power both RAG and the overlay launcher.

---

## Startup sequence

1. `App` constructor wires crash logging (three fatal-exception channels routed to
   `%TEMP%\WindowsAI-crash.log`) and builds the DI container.
2. The app starts to the **tray** — no top-level window is shown.
3. The always-alive overlay is created and the global hotkeys are registered.
4. `ThemeSyncService` begins mirroring the Windows light/dark and accent settings into the live
   brush resources.
5. `Alt+Space` toggles the overlay; the tray menu opens the full app or quits.

> Fatal exceptions are deliberately **not** swallowed. An unhandled exception means unknown state,
> and masking it risks corrupting user data, so the app logs the stack and fails fast. Before a
> public release the file log should be superseded by a consent-based crash reporter.

---

## Runtime data layout

| Data | Location | Format and protection |
| :--- | :--- | :--- |
| Settings | `%LOCALAPPDATA%\WindowsAI\` | Plain JSON |
| Conversations | `%LOCALAPPDATA%\WindowsAI\conversations\` | One JSON file per conversation, auto-titled |
| Secrets | `%LOCALAPPDATA%\WindowsAI\secrets\` | DPAPI-encrypted, per-user |
| Crash log | `%TEMP%\WindowsAI-crash.log` | Appended plain text |

---

## Design system

The visual language is **Electric Iris**: an indigo-violet accent (`#6E7BFF`) over layered glass,
set in Inter. Tokens (color, radii, motion timings) live in `Themes/Tokens.xaml` and `Palette.xaml`;
`ThemeSyncService` is the runtime source of truth for brush values, keeping every window in step
with the Windows theme and accent live.

See the [App project documentation](../src/WindowsAI.App/README.md) for the presentation details.
