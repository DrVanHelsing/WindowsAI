<div align="center">

# WindowsAI.Core

The platform-agnostic engine. Chat orchestration, model providers, conversation history,
settings, retrieval, and writing transforms — with no dependency on Windows UI.

![Target](https://img.shields.io/badge/net10.0-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)
![Layer](https://img.shields.io/badge/Core%20engine-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)

[![Home](https://img.shields.io/badge/%E2%86%90%20%20README-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)](../../README.md)
&nbsp;
[![Architecture](https://img.shields.io/badge/Architecture-0B0E14?style=for-the-badge&labelColor=0B0E14&color=B06BFF)](../../docs/ARCHITECTURE.md)

</div>

---

## Responsibility

This is the heart of WindowsAI. It targets plain `net10.0` and deliberately avoids any Windows UI
or interop, so it can be unit-tested in isolation and reused on any surface. Where it needs a
platform capability — encrypted storage, local file retrieval — it declares an **abstraction** and
lets the host supply the implementation.

It builds on `Microsoft.Extensions.AI` for the chat client abstraction. Providers are **data-driven**:
the user configures any number of `ProviderConnection`s through the UI, and the engine builds a
runtime provider for each — one generic OpenAI-compatible adapter plus Ollama (local) and Vertex
(Google sign-in) — so it works with any compatible backend without code changes.

---

## Folder map

| Folder | Contents |
| :--- | :--- |
| `Chat/` | `ChatOrchestrator` — resolves a capability's model via `CapabilityRouter` and streams replies, including the agent tool-calling loop. |
| `Providers/` | `IChatProvider`; the connection providers (`OpenAiConnectionProvider`, `OllamaConnectionProvider`, `VertexConnectionProvider`); `IProviderRegistry`, `CapabilityRouter`, `ProviderPresets`, `ProviderProbe`; the model catalog, `ActiveModelService`, and the vision and image services. |
| `Configuration/` | `AppSettings` (data-driven `ProviderConnection`s, `CapabilityRouting`, `FeatureSettings`, `LocalIndexSettings`, `OnboardingState`), the `ISettingsStore` abstraction + JSON store, and `SettingsMigrator`. |
| `History/` | `IConversationStore` and `JsonConversationStore` — one JSON file per conversation, with pinning and auto-titling. |
| `Context/` | Local-RAG contracts: `ILocalContextRetriever`, `IEmbeddingClient`, `LocalDocument`, the file text extractor, and `RerankingContextRetriever`. |
| `Search/` | Overlay launcher support: prompt classification, search hits, and the recents store. |
| `Security/` | `ISecretStore` abstraction and `SecretKeys` — the host provides the concrete store. |
| `Auth/` | `GoogleOAuthService` — the desktop OAuth loopback flow for Gemini via Vertex AI. |
| `Tools/` | `WebSearchService` backing the agent's `search_web` tool. |
| `Writing/` | The writing-tools engine: the `WritingAction` catalog, the `WritingPrompt` builder, and `WritingToolsService`. |

---

## Key types

| Type | Role |
| :--- | :--- |
| `ChatOrchestrator` | Central chat entry point. Resolves the capability's model via `CapabilityRouter`; streams text deltas; runs the tool-calling loop with a one-shot fallback for models that reject tools. |
| `IChatProvider` | A backend: id, display name, an `IChatClient` factory, and vision / image-generation / default-model metadata. |
| `IProviderRegistry` | Builds and caches one provider per enabled `ProviderConnection`; `Invalidate()` rebuilds after edits. |
| `CapabilityRouter` | Resolves the provider + model for each capability (Chat, Visual Intelligence, Writing, Image, Embeddings), with fallbacks. |
| `ProviderPresets` / `ProviderProbe` | The preset catalog that pre-fills the Add-provider form; live model discovery + connection testing. |
| `SettingsMigrator` | Upgrades a v1 settings file to the data-driven schema in place, preserving stored keys. |
| `ModelCatalog` | Builds the picker list from configured connections (live Ollama tags + curated cloud models). |
| `ActiveModelService` | Single source of truth for the chat model; raises `Changed` so every surface updates live. |
| `JsonConversationStore` | Persists conversations as JSON, pinned-first ordering, per-conversation overrides. |
| `RerankingContextRetriever` | Wraps a keyword retriever and re-orders candidates by local embedding similarity. |
| `WritingToolsService` | Streams a one-shot text transform (proofread, rewrite, tone, summarize) through the Writing Tools capability. |
| `VisionService` | Routes a vision request to the Visual Intelligence capability's model (auto-picks a vision-capable provider). |
| `GoogleOAuthService` | Per-connection desktop OAuth flow; persists the refresh token through the host's `ISecretStore`. |

---

## Abstractions the host must satisfy

The core registers most of its own services via `AddWindowsAiCore()`, but two contracts are left
for the host to implement with platform code:

| Abstraction | Implemented by | Purpose |
| :--- | :--- | :--- |
| `ISecretStore` | `DpapiSecretStore` (Services) | Encrypted storage for API keys and OAuth tokens |
| `ILocalContextRetriever` | Composed in the App from `WindowsSearchRetriever` + `RerankingContextRetriever` | Local file retrieval for RAG |

---

## Registration

```csharp
using WindowsAI.Core;

services.AddWindowsAiCore();
// then supply the platform pieces:
services.AddSingleton<ISecretStore, DpapiSecretStore>();
```

---

## Dependencies

`Microsoft.Extensions.AI` (and the OpenAI adapter), `Microsoft.Extensions.DependencyInjection`,
`Options`, and `Logging` abstractions, `Google.Apis.Auth` (OAuth), `OllamaSharp` (local models),
and `PdfPig` (PDF text extraction for RAG).

---

## Tests

Core is covered by [`tests/WindowsAI.Core.Tests`](../../tests/WindowsAI.Core.Tests/README.md) (xUnit)
and exercised end to end by the [smoke runner](../../tools/WindowsAI.Smoke/README.md).
