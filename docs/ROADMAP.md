# WindowsAI — Roadmap & Status

A Windows-native, overlay-first AI assistant (Apple-Intelligence-style) built on WinUI 3 / .NET 10.
The app lives in the tray and is summoned with **Alt+Space**; a full NavigationView app sits behind it.

**Legend:** ✅ Done · 🚧 In progress · ⏭ Next · 💡 Later

Status reflects the codebase as of the current working session. "Built, not yet run" means the
code compiles cleanly but has not been visually verified by launching the app.

---

## Architecture at a glance

- **`WindowsAI.Core`** — platform-agnostic: chat orchestration, providers (Ollama / OpenRouter /
  Gemini via `Microsoft.Extensions.AI`), settings, secrets abstraction, conversation history,
  active-model service.
- **`WindowsAI.Services`** — Windows-specific interop: global hotkey, window chrome, DPAPI secrets.
- **`WindowsAI.App`** — WinUI 3 UI: the overlay window, the NavigationView shell, view models.

Key DI lifetimes: `ChatViewModel` is a **singleton** shared by the shell (Chat + Models pages);
the **overlay builds its own instance** (`App.CreateChatViewModel`) so its conversation is
independent. Both persist into the same history. The active model is a single source of truth
(`ActiveModelService`) so a change on any surface updates the others live.

---

## Phase 0 — Foundation ✅

- ✅ Solution scaffold: `App` (WinUI 3 / .NET 10), `Core`, `Services` — builds clean.
- ✅ Multi-provider chat via `Microsoft.Extensions.AI`: Ollama (local), OpenRouter, Gemini.
  Gemini (AI Studio API key) **live-verified end to end 2026-06-15** — `gemini-3.5-flash` streams replies.
- ✅ Model switcher + Settings page with **DPAPI-encrypted API keys**.
- ✅ OpenRouter cloud path **verified working in-app** (`google/gemma-4-31b-it:free`).

## Phase 1 — Overlay-first UI ✅ (built; end-to-end run pending)

- ✅ Win32 interop (`NativeMethods`, `HotkeyManager` = Alt+Space, `WindowChrome` drag/resize/monitor).
- ✅ Design tokens (`Themes/Tokens.xaml`): signature glow gradient, radii, motion timings.
- ✅ `OverlayWindow`: borderless, acrylic, always-on-top, rotating "running glow", summon/dismiss
  animations, drag handle, resize grip, tray icon + menu, Esc / focus-loss dismiss.
- ✅ `App.xaml.cs` overlay-first startup: starts to **tray** (no main window shown), creates the
  always-alive overlay, registers the global hotkey (Alt+Space → toggle overlay).
- ✅ Tray menu actions wired (`ShowMainApp`, `QuitApp`).

## Phase 2 — Full app shell ✅ (built; end-to-end run pending)

- ✅ `NavigationView` shell (`MainWindow`): **Chat / History / Models / Settings**, entrance transitions.
- ✅ **Chat** page (`MainPage`) with inline model switcher + **New chat** button.
- ✅ **Models** page: selectable list of local/cloud models with badges + refresh.
- ✅ **Settings** page: local endpoint + DPAPI-secured cloud keys.

## Phase 3 — Conversations ✅ (built; end-to-end run pending)

- ✅ Persistence: `IConversationStore` / `JsonConversationStore` — one JSON file per conversation
  under `%LOCALAPPDATA%\WindowsAI\conversations\`. Auto-titled from the first user message.
- ✅ Auto-save after each completed exchange; load / delete from the **History** page.
- ✅ Overlay keeps an **independent** conversation; both overlay and shell save into shared history.
- ✅ **Live active-model sync** across shell + overlay via `ActiveModelService`.

## Local model

- ✅ `gemma4:e2b-it-qat` pulled (4.3 GB) and **inference verified at the Ollama layer** — one-shot
  CLI generation returns a correct, coherent reply.
- 🚧 **In-app local chat not yet verified** (needs a GUI run — same bucket as the verification below).

  Findings:
  1. **Ollama server autostart.** The server was *not* running and had to be cold-started; the local
     provider assumes a live server at `http://localhost:11434`. Confirm Ollama's tray app
     auto-starts at login, or have the app detect a down server and surface a clear message.
  2. ✅ **Reasoning traces do not leak.** Verified via the Core smoke test: streaming
     `gemma4:e2b-it-qat` through `ChatOrchestrator` returns a clean `"OK"` with no `<think>` content
     (`update.Text` excludes `TextReasoningContent` as designed).

## Automated test harness ✅

`tools/WindowsAI.Smoke` — a headless console runner that exercises the Core paths the UI depends on.
Run with `dotnet run --project tools/WindowsAI.Smoke`. Current result: **19/19 checks pass** (with local
Ollama + `nomic-embed-text` available), covering settings round-trip, conversation save/list/get/delete,
active-model persistence + `Changed` event (incl. no-op on unchanged value), Windows Search retrieval +
embedding re-rank, catalog discovery (local + cloud), live local streaming, and the agent tool-calling
loop (scripted stub client → tool invoked with the right args → final reply reflects the result).

## 🚧 Currently in progress

- 🚧 **End-to-end verification (manual relaunch).** All of Phases 1–3 compile cleanly but have not
  been run yet. Need to confirm: tray-only startup, Alt+Space summon/dismiss, navigation between
  the four pages, open-from-history → switches to Chat tab, new-chat, live model-chip updates, and
  **local chat against `gemma4:e2b-it-qat`** (see findings above).

## Phase 4 — Local-first RAG 🚧

- ✅ **Windows Search retrieval** (`WindowsSearchRetriever`): queries the live SYSTEMINDEX
  over OLE DB (FREETEXT, user-profile scope, rank order), snippets from index AutoSummary
  or plain-text file heads. Covered by a smoke check against the real index.
- ✅ **Chat integration**: `ILocalContextRetriever` (Core) → opt-in "Search my PC" toggle in
  the composer and overlay pill → matching files injected as a transient system message →
  source chips rendered under the reply.
- ✅ **Embedding re-rank** (`OllamaEmbeddingClient` + `RerankingContextRetriever`): over-fetches a
  keyword pool from the index, then re-orders by cosine similarity of local `nomic-embed-text`
  embeddings (hybrid recall → semantic precision); falls back to keyword order if embeddings are
  unavailable. Covered by a smoke check.

## Phase 5 — Agent tool-calling ✅ (built; cloud-model run pending)

- ✅ **Tool-calling loop** in `ChatOrchestrator` via Microsoft.Extensions.AI's
  `FunctionInvokingChatClient` — `StreamReplyAsync` takes optional `AITool`s; models that reject
  tools (e.g. Gemma via Ollama) fall back to a plain reply before the first token.
- ✅ **Tools** (`AgentToolFactory`, built per-send so the shell and overlay update independently):
  `search_my_pc` (wraps the Windows Search RAG; drives the "Searching your PC…" sparkle + source
  chips) and `open_path` (opens a file/folder in its default app — **constrained** to paths a prior
  `search_my_pc` actually surfaced this conversation).
- ✅ Exposed only when the **Search my PC** toggle is on (privacy + keeps the default local path
  tool-free). Covered by a provider-independent smoke check (scripted stub client + real tool).

## Phase 6 — Type-anywhere writing tools ✅ (built; cross-app paste run pending)

- ✅ **Core transforms** (`WindowsAI.Core/Writing`): declarative `WritingAction` catalog
  (proofread / rewrite / friendly / professional / concise / summarize / key points), a pure
  `WritingPrompt` builder with strict "return only the text" instructions for edge models +
  `CleanResult` (strips stray quotes / code fences), and `WritingToolsService` streaming a
  one-shot transform through the active model.
- ✅ **Selection bridge** (`WindowsAI.Services/Writing/TextSelectionService`): synthesizes the
  Ctrl+C / Ctrl+V chords (SendInput) so reading and replacing the selection work across Win32,
  WinUI, browser, and Electron apps — the clipboard route, not the patchy UIA TextPattern.
- ✅ **Popup + hotkey**: `Ctrl+Shift+Space` over a selection opens a borderless, theme-following
  writing-tools menu near the cursor (`WritingToolsWindow`, `WritingToolsController`); the chosen
  action streams a result with Back / Copy / Replace. `HotkeyManager` now multiplexes multiple
  global chords over one window subclass.
- ✅ **Verified by screen capture**: menu + streamed Proofread result render on-brand and
  `gemma4:e2b` corrected the sample text.
- 🚧 **Cross-app paste injection pending manual check** — the final Ctrl+V into a third-party app
  needs real (trusted) keyboard input, which the automated QA harness can't synthesize.

## Phase 7 — Visual intelligence ✅ (built; live vision round-trip pending)

- ✅ **Region capture** (`WindowsAI.Services/Capture/ScreenCaptureService`): grabs the monitor under
  the cursor via GDI (`CopyFromScreen`) into a frozen bitmap, then crops the user's selection to a
  PNG. Capturing up front means the selection is drawn against a still image and we never capture
  our own dimming overlay.
- ✅ **Capture surface** (`RegionCaptureWindow`): borderless, always-on-top, fullscreen over the
  cursor's monitor. Shows the frozen screenshot, dims it with a four-rectangle scrim that
  "spotlights" the dragged selection, and reads the cursor monitor's DPI to map the DIP selection
  back to physical pixels for the crop. **Verified by screen capture** — dim+banner state and the
  drag → spotlight → ask card both render on-brand.
- ✅ **Ask card + vision model** (`WindowsAI.Core/Providers/VisionService`): after a region is
  selected, an inline card shows the crop thumbnail, a question box, and a streamed answer. The
  request always runs on a **configurable OpenRouter vision model** (`OpenRouterSettings.VisionModelId`,
  default `google/gemma-4-31b-it:free` — a strong free multimodal all-rounder; swap to
  `nvidia/nemotron-nano-12b-v2-vl:free` for faster OCR/document-heavy use) regardless of the active
  chat model — visual intelligence needs a multimodal backend even when the everyday model is local
  text-only.
- ✅ **Hotkey + Settings**: `Alt+Shift+S` (`HotkeyDefinition.VisualIntelligence`, multiplexed through
  the same `HotkeyManager` subclass) opens the capture; the vision model id is editable on the
  Settings page under OpenRouter.
- 🚧 **Live vision round-trip pending** — needs an OpenRouter key + a real model call; the streaming
  path reuses the proven `OpenRouterChatProvider` image-attachment client.

## Gemini via Google sign-in (Vertex AI, OAuth) ✅

An alternative to a pasted Gemini API key: sign in with Google and reach Gemini through **Vertex AI's
OpenAI-compatible endpoint** with a short-lived OAuth access token.

- ✅ **OAuth loopback flow** (`WindowsAI.Core/Auth/GoogleOAuthService`): the desktop "installed app"
  flow (`AuthorizationCodeInstalledApp` + `LocalServerCodeReceiver` from `Google.Apis.Auth`) opens the
  system browser for consent. The refresh token is persisted **DPAPI-encrypted** via a custom
  `IDataStore` backed by `ISecretStore` (scope `cloud-platform`). Access tokens are minted/refreshed
  silently thereafter.
- ✅ **Provider** (`WindowsAI.Core/Providers/VertexChatProvider`, id `vertex`): builds the project- +
  region-scoped endpoint `https://{region}-aiplatform.googleapis.com/v1beta1/projects/{project}/
  locations/{region}/endpoints/openapi` (the `global` region drops the host prefix) and passes the
  OAuth token as the bearer. Reuses the shared OpenAI client; `SupportsVision = true`.
- ✅ **Settings** (`GoogleCloudSettings`): the user creates an OAuth "Desktop app" client in their own
  Google Cloud project and enters the **client id, client secret** (secret → `ISecretStore`),
  **project id**, and **region** in Settings → "Gemini via Google sign-in", then clicks Sign in.
- ✅ **Models** (verified live via Model Garden + a round-trip on the Vertex OpenAI-compat endpoint,
  2026-06-15): defaults are **`google/gemini-3.5-flash`** (latest GA flash) with
  **`google/gemini-3.1-pro-preview`** in the picker. **Region defaults to `global`** — the 3.x
  generation is only served there; `us-central1` 404s on it (2.5 still works regionally).
- 🚧 **In-app sign-in round-trip pending** — the ADC→Vertex chain + both 3.x model ids were confirmed
  live; what's untested is the loopback OAuth consent inside the app, which needs the user's own
  OAuth "Desktop app" client id/secret.

## Unit tests ✅

`tests/WindowsAI.Core.Tests` (xUnit) — the first dedicated unit-test project. **11 tests** cover the
writing-tools action catalog, prompt shape, and result cleaning. Run with
`dotnet test tests/WindowsAI.Core.Tests`.

## ⏭ Next

- ⏭ Manually verify writing-tools paste injection across Notepad / Word / Chrome / VS Code.
- ⏭ Live visual-intelligence round-trip: add an OpenRouter key, capture a region, confirm a real
  vision-model answer streams (and tune the default free model if it's been deprecated upstream).
- ⏭ Google sign-in live round-trip: user adds their OAuth "Desktop app" client id + secret and a
  Vertex-enabled project, signs in, and confirms `google/gemini-3.5-flash` answers via Vertex.
  (The API-key path to the same models is already verified — this only exercises the OAuth consent.)
- ⏭ More tools (web search, current date/time); per-tool surfacing of agent steps in the transcript.

## 💡 Later

- 💡 History niceties: rename, search, pin.
- 💡 Per-conversation model/system-prompt overrides.
- 💡 Streaming cancellation (stop button) and retry.
