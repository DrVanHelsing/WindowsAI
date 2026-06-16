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
  the **provider registry** + probe + **capability router**, the model catalog, the active-model
  service, the chat orchestrator, and the writing, vision, image, and web-search services.
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

All chat flows through `ChatOrchestrator` in the core. It resolves the model for a capability (chat
by default) via `CapabilityRouter` and streams the reply token by token.

```
 ChatViewModel
     |  builds the message list (system + history + user, plus any RAG context)
     v
 ChatOrchestrator.StreamReplyAsync(messages, tools?, capability = Chat)
     |  CapabilityRouter resolves provider + model id for the capability
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

Providers are **data-driven**: instead of a fixed class per backend, the user configures an
arbitrary list of `ProviderConnection`s through the UI, and the app builds a runtime `IChatProvider`
for each. This is what lets WindowsAI talk to *any* provider — OpenAI, OpenRouter, Gemini, Anthropic,
Groq, DeepSeek, Mistral, local LM Studio / Ollama, Azure, or a custom endpoint — with no code change.

A `ProviderConnection` (persisted in settings) carries an id, display name, `ProviderKind`, base URL,
enabled flag, capability hints (vision / image generation), a curated model list, and a default
model. Three kinds cover everything:

| `ProviderKind` | Runtime provider | Backend |
| :--- | :--- | :--- |
| `OpenAiCompatible` | `OpenAiConnectionProvider` | Any OpenAI chat-completions endpoint (OpenAI, OpenRouter, Gemini-compat, Anthropic, Groq, LM Studio, vLLM, …) |
| `Ollama` | `OllamaConnectionProvider` | Local Ollama server; live model list via `/api/tags`; no key |
| `GoogleVertex` | `VertexConnectionProvider` | Gemini via Vertex AI, authenticated with a Google sign-in (OAuth) |

The supporting pieces:

- **`IProviderRegistry`** builds and caches one `IChatProvider` per *enabled* connection, exposing
  `All` / `GetById` / `GetConfigured`. Editing the connection list and calling `Invalidate()` rebuilds
  it — no DI re-registration.
- **`ProviderPresets`** is a pure-data catalog (OpenAI, OpenRouter, Gemini, Anthropic, Groq, DeepSeek,
  Mistral, Together, xAI, Azure, Ollama, LM Studio, Custom, Gemini-via-Google-sign-in) used only to
  pre-fill the "Add provider" form and the onboarding gallery.
- **`ProviderProbe`** lists models (`GET /models`, or Ollama `/api/tags`) and powers the dialog's
  **Test connection** / **Fetch models** with clear ok / auth-failed / unreachable results.
- **`ModelCatalog`** builds the picker list from the configured connections (live Ollama tags +
  curated cloud models). **`ActiveModelService`** holds the current chat selection and broadcasts changes.
- Secrets are keyed per connection (`SecretKeys.ApiKeyFor(id)`); the seeded built-ins reuse the
  original v1 key names so a migrated user keeps their stored keys.

### Schema migration

`AppSettings` is versioned (`SchemaVersion`). On load, `SettingsMigrator` upgrades an older v1 file
(fixed per-provider fields) in place: it seeds `ProviderConnection`s from the legacy fields — reusing
the well-known ids `ollama`/`openrouter`/`gemini`/`vertex` so DPAPI keys survive — and maps the old
scattered model ids onto the new capability routing. A fresh install is seeded with just a local
Ollama connection; the onboarding wizard adds the rest.

---

## Feature routing and flags

Each AI capability picks its **own** model from any configured provider, resolved by
`CapabilityRouter` over `CapabilityRouting` in settings:

| Capability | Default | Fallback when unset |
| :--- | :--- | :--- |
| Chat | the everyday model | first configured provider |
| Visual Intelligence | explicit pick | first configured vision-capable provider |
| Writing Tools | explicit pick | the chat model |
| Image Generation | explicit pick | first configured image-capable provider |
| Embeddings | explicit pick (local Ollama) | off (RAG falls back to keyword order) |

This replaces the old hardcoded vision-priority list — `VisionService` and `ImageGenerationService`
now resolve through the router, so they work with whatever the user configured.

`FeatureSettings` carries an enable flag per feature (Visual Intelligence, Writing Tools, Web Search,
Image Generation, PC Indexing) plus the **rebindable** global hotkey chords. Disabled features are
not offered as tools and their hotkeys are not registered. Everything is set in the first-run wizard
and editable later in Settings.

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
shell and overlay update independently. Each tool is offered only when its **feature flag** is on
(and any key is configured).

| Tool | Purpose | Gated by |
| :--- | :--- | :--- |
| `search_my_pc` | Runs the Windows Search RAG | PC Indexing feature + the composer's "Search my PC" toggle |
| `open_path` | Opens a file or folder | Constrained to paths a prior `search_my_pc` surfaced this conversation |
| `get_current_datetime` | Returns the local date and time | Always offered |
| `search_web` | Tavily Search | Web Search feature + a configured Tavily key; results render as link chips |

---

## Windows interop

`WindowsAI.Services` isolates everything that touches Win32 or Windows-only APIs:

- **Hotkeys** — `HotkeyManager` multiplexes multiple global chords over one window subclass; the
  chords come from `FeatureSettings` (rebindable; defaults `Alt+Space`, `Ctrl+Shift+Space`,
  `Alt+Shift+S`) and disabled features register no chord.
- **Window chrome** — `WindowChrome` and `NativeMethods` handle borderless windows, drag, resize,
  and monitor placement.
- **Capture** — `ScreenCaptureService` grabs the monitor under the cursor via GDI into a frozen
  bitmap, then crops the user's selection.
- **Tray** — `NativeTrayIcon` provides the tray presence and menu.
- **Secrets** — `DpapiSecretStore` encrypts keys and tokens per-user with Windows DPAPI.
- **Search** — `WindowsSearchRetriever`, `AppCatalog`, `FileNameSearch`, and `SettingsCatalog`
  power both RAG and the overlay launcher.
- **Index management** — `WindowsSearchIndexService` drives the Windows Search Manager COM API
  (`CSearchManager` → SystemIndex catalog → crawl-scope manager) to add/remove folders from the
  OS index scope and read its item counts for the PC Indexing progress UI. The app builds no index
  of its own; it manages the OS scope and is failure-silent if Windows Search is off.

---

## Startup sequence

1. `App` constructor wires crash logging (three fatal-exception channels routed to
   `%TEMP%\WindowsAI-crash.log`) and builds the DI container.
2. Settings load; `SettingsMigrator` upgrades/seeds the schema as needed.
3. The app starts to the **tray** — no top-level window is shown.
4. The always-alive overlay is created and the **enabled** features' global hotkeys are registered
   from `FeatureSettings`.
5. `ThemeSyncService` begins mirroring the Windows light/dark and accent settings into the live
   brush resources.
6. **First run only:** if `Onboarding.HasCompleted` is false, the `OnboardingWindow` setup wizard is
   shown (welcome → connect a provider → choose features → PC indexing → personalize → done). It
   persists its own completion flag; everything it sets is editable later in Settings.
7. `Alt+Space` (or the user's chord) toggles the overlay; the tray menu opens the full app or quits.

> Fatal exceptions are deliberately **not** swallowed. An unhandled exception means unknown state,
> and masking it risks corrupting user data, so the app logs the stack and fails fast. Before a
> public release the file log should be superseded by a consent-based crash reporter.

---

## Runtime data layout

| Data | Location | Format and protection |
| :--- | :--- | :--- |
| Settings | `%LOCALAPPDATA%\WindowsAI\settings.json` | Plain JSON, versioned (`SchemaVersion`); migrated in place on load |
| Conversations | `%LOCALAPPDATA%\WindowsAI\conversations\` | One JSON file per conversation, auto-titled |
| Secrets | `%LOCALAPPDATA%\WindowsAI\secrets\` | DPAPI-encrypted, per-user |
| Crash log | `%TEMP%\WindowsAI-crash.log` | Appended plain text |

---

## Packaging and distribution

Two paths share one codebase:

- **MSIX (packaged)** — the default for `dotnet run` and Store-style distribution; the app runs with
  package identity (`Package.appxmanifest`).
- **Unpackaged installer** — `installer/build.ps1` runs `dotnet publish` with
  `WindowsPackageType=None` + `WindowsAppSDKSelfContained=true` + `SelfContained=true`, producing a
  folder that runs on a clean PC with no MSIX identity and no separately-installed .NET / WindowsApp
  SDK runtime (self-containing the runtime + bootstrap auto-init avoids the historical bare-exe
  `REGDB_E_CLASSNOTREG` crash). `installer/WindowsAI.iss` then compiles a classic Inno Setup `.exe`:
  Program Files install, Start Menu / optional desktop shortcuts, opt-in run-at-startup, a WebView2
  runtime check, and an uninstaller that **preserves** `%LOCALAPPDATA%\WindowsAI` user data.

---

## Design system

The visual language is **Electric Iris**: an indigo-violet accent (`#6E7BFF`) over layered glass,
set in Inter. Tokens (color, radii, motion timings) live in `Themes/Tokens.xaml` and `Palette.xaml`;
`ThemeSyncService` is the runtime source of truth for brush values, keeping every window in step
with the Windows theme and accent live.

See the [App project documentation](../src/WindowsAI.App/README.md) for the presentation details.
