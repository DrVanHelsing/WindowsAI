<div align="center">

# Setup Guide

Everything needed to build, run, and configure WindowsAI — from a bare machine to a fully
wired assistant with local and cloud models, web search, and visual intelligence.

[![Home](https://img.shields.io/badge/%E2%86%90%20%20README-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)](../README.md)
&nbsp;
[![Architecture](https://img.shields.io/badge/Architecture-0B0E14?style=for-the-badge&labelColor=0B0E14&color=B06BFF)](ARCHITECTURE.md)
&nbsp;
[![Roadmap](https://img.shields.io/badge/Roadmap-0B0E14?style=for-the-badge&labelColor=0B0E14&color=B06BFF)](ROADMAP.md)

</div>

---

## Contents

1. [Prerequisites](#1-prerequisites)
2. [Build and run](#2-build-and-run)
3. [First run — the setup wizard](#3-first-run--the-setup-wizard)
4. [Add a provider](#4-add-a-provider)
   - [The Add-provider dialog](#41-the-add-provider-dialog)
   - [Cloud API key (OpenRouter, OpenAI, Gemini, …)](#42-cloud-api-key-openrouter-openai-gemini-)
   - [Local model (Ollama / LM Studio)](#43-local-model-ollama--lm-studio)
   - [Gemini via Google sign-in (Vertex AI, OAuth)](#44-gemini-via-google-sign-in-vertex-ai-oauth)
5. [Configure features](#5-configure-features)
   - [Per-feature models](#51-per-feature-models)
   - [Web search](#52-web-search)
   - [PC indexing](#53-pc-indexing)
   - [Hotkeys](#54-hotkeys)
6. [Build a distributable installer](#6-build-a-distributable-installer)
7. [Where your data lives](#7-where-your-data-lives)
8. [Troubleshooting](#8-troubleshooting)

> **A local model is optional, not required — and so is every cloud provider.** Pick whatever suits
> you: a cloud API key, a Google sign-in, or a local Ollama model. You can add as many providers as
> you like and change everything later.

---

## 1. Prerequisites

| Requirement | Why | How |
| :--- | :--- | :--- |
| **Windows 11** | Mica and the rounded-glass chrome target Windows 11. Windows 10 (1809+) runs with reduced backdrop fidelity. | Built in. |
| **.NET 10 SDK** | The solution targets `net10.0` and `net10.0-windows10.0.26100.0`. | Download from [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download/dotnet/10.0). Verify with `dotnet --version`. |
| **Windows App SDK 1.8** | WinUI 3 runtime. | Restored automatically via NuGet on first build — no manual install. |
| **Developer Mode** *(recommended)* | Lets the app register a debug package identity for `dotnet run`. | Settings → System → For developers → Developer Mode → On. |
| **Git** | Cloning the repository. | [git-scm.com](https://git-scm.com/). |
| **Ollama** *(optional, for local models)* | Fully offline chat, RAG, and writing tools. | See [4.1](#41-local-model-ollama). |

---

## 2. Build and run

```powershell
git clone https://github.com/DrVanHelsing/WindowsAI.git
cd WindowsAI

# Build (specify the platform explicitly — see Troubleshooting if you skip it)
dotnet build src\WindowsAI.App\WindowsAI.App.csproj -c Debug -p:Platform=x64
```

Launch the app **through the .NET CLI** so it starts with package identity (AUMID). Running the
produced `.exe` directly is not supported and will crash.

```powershell
dotnet run --project src\WindowsAI.App\WindowsAI.App.csproj
```

The app starts silently to the system tray — no window appears. Press `Alt + Space` to summon the
overlay, or open the full app from the tray icon's menu.

### Run the tests (optional)

```powershell
dotnet test tests\WindowsAI.Core.Tests          # fast, offline unit tests
dotnet run --project tools\WindowsAI.Smoke       # integrated smoke runner (needs Ollama for the full set)
```

---

## 3. First run — the setup wizard

The first launch opens a **setup wizard** (it appears once; reach the same settings anytime
afterward). It walks through, and every step is skippable:

1. **Welcome.**
2. **Connect your AI** — add a provider (cloud key, Google sign-in, or local Ollama). See [§4](#4-add-a-provider).
3. **Choose your features** — toggle Visual Intelligence, Writing Tools, Web Search, Image
   Generation, and PC Indexing.
4. **PC indexing** — pick folders for "Search my PC" (only if you enabled it).
5. **Personalize** — theme and accent.
6. **Done** — a reminder of the global shortcuts.

After the wizard the app lives in the **tray**. Global shortcuts (all rebindable in Settings):

| Shortcut | Action |
| :--- | :--- |
| `Alt + Space` | Summon or dismiss the overlay |
| `Ctrl + Shift + Space` | Writing tools over the current text selection |
| `Alt + Shift + S` | Capture a screen region and ask about it |
| `Esc` | Dismiss the overlay or any floating surface |

---

## 4. Add a provider

Providers are fully data-driven — add as many as you like through the UI, from a one-click preset or
a custom endpoint. Everything lives on the **Models & providers** tab (in the app shell) or the
wizard's Connect step. API keys and OAuth tokens are encrypted with Windows DPAPI and never leave
this PC. After adding providers, pick your default chat model in the **Models** list (it is shared
across the overlay, chat page, and models page).

### 4.1 The Add-provider dialog

1. On **Models & providers**, click **+ Add provider** (or **+ Add a provider** in the wizard).
2. Pick a **preset** — OpenAI, OpenRouter, Google Gemini, Anthropic (Claude), Groq, DeepSeek,
   Mistral, Together, xAI, Azure OpenAI, Ollama, LM Studio, Gemini via Google sign-in — or
   **Custom (OpenAI-compatible)** for any other endpoint. The form pre-fills the base URL,
   suggested models, and capability flags.
3. Paste the **API key** (if the provider needs one), then click **Test connection** for an instant
   ok / auth-failed / unreachable result, and **Fetch models** to pull the live model list.
4. Set the **default model**, tick **vision** / **image generation** if the endpoint supports them,
   and **Add**.
5. Each provider card shows a status dot (connected / needs key / disabled), an enable toggle, and
   **Edit / Test / Remove**.

### 4.2 Cloud API key (OpenRouter, OpenAI, Gemini, …)

Use a preset and paste the key from the provider's console — e.g. [openrouter.ai](https://openrouter.ai)
(**Keys**), [OpenAI](https://platform.openai.com/api-keys), [Google AI Studio](https://aistudio.google.com/apikey),
[Anthropic](https://console.anthropic.com/settings/keys), [Groq](https://console.groq.com/keys). The
key is stored encrypted (DPAPI), local only; remove it anytime from the card.

### 4.3 Local model (Ollama / LM Studio)

A fully offline path — optional, and no key required.

1. Install [Ollama](https://ollama.com/download) (or [LM Studio](https://lmstudio.ai)) and pull a model:

   ```powershell
   ollama pull gemma3            # any chat model works; pick to taste
   ollama pull nomic-embed-text  # enables semantic re-rank for "Search my PC"
   ```

2. Confirm the server responds (`ollama list`; default `http://localhost:11434`).
3. Add the **Ollama (local)** preset (or **LM Studio (local)**). The endpoint defaults to localhost;
   live models are listed automatically. Local OpenAI-compatible servers need no key.
4. Pick the model in **Models**. You are ready to chat offline.

> If a model rejects tool-calling (some local models do), the app automatically falls back to a
> plain reply — agent tools simply stay inactive for that model.

### 4.4 Gemini via Google sign-in (Vertex AI, OAuth)

Reach Gemini through Vertex AI with a Google sign-in rather than a pasted key. This needs a one-time
setup of your own OAuth client in Google Cloud. The app opens your browser for consent and stores
only a refresh token (DPAPI-encrypted).

**In the [Google Cloud Console](https://console.cloud.google.com):**

1. Create (or pick) a project, and note its **Project ID**.
2. Enable the **Vertex AI API** for that project (APIs & Services → Enable APIs → search "Vertex AI").
3. Configure the **OAuth consent screen** (External is fine for personal use; add your Google
   account as a test user).
4. Create credentials → **OAuth client ID** → application type **Desktop app**. Copy the generated
   **client ID** and **client secret**.

**In WindowsAI** — add the **Gemini via Google sign-in** preset (in the Add-provider dialog or the
wizard) and fill in:

5. **OAuth client id** and **client secret** (the secret is stored encrypted via DPAPI).
6. **Google Cloud project id**.
7. **Vertex region** → **`global`**. The Gemini 3.x generation is only served on `global`; regional
   endpoints such as `us-central1` return 404 for those models.
8. Save, then click **Sign in** on the provider card and complete the browser consent. The status
   dot turns connected.
9. Pick a Vertex model in **Models** (defaults to `google/gemini-3.5-flash`).

> Use **Remove** on the card (or sign out) to revoke the stored token at any time.

---

## 5. Configure features

Each feature has an on/off toggle and its own settings page. Onboarding sets initial choices;
**Settings** is where you fine-tune.

### 5.1 Per-feature models

Route each capability to whichever configured model you prefer:

| Settings page | What it sets |
| :--- | :--- |
| **Chat** | The default everyday model |
| **Visual Intelligence** | A **vision-capable** model for `Alt + Shift + S` (defaults to "auto — first vision-capable provider") |
| **Writing Tools** | The model for `Ctrl + Shift + Space` (defaults to the chat model) |
| **Image Generation** | An **image-capable** model for in-chat image prompts |

The pickers only list providers that advertise the matching capability, so you can mix (e.g. a local
model for chat, a cloud vision model for screen captures).

### 5.2 Web search

Lets the assistant search the web via [Tavily](https://tavily.com) (the agent `search_web` tool).

1. Enable **Web Search** in Settings.
2. Create a free **API key** at [tavily.com](https://tavily.com) (keys start with `tvly-`).
3. **Settings → Web Search → Manage…** → paste the key (encrypted via DPAPI).
4. Ask a cloud model something current; the `search_web` step and result link chips appear above the reply.

### 5.3 PC indexing

"Search my PC" uses the **Windows Search index** — the app manages which folders are in scope rather
than building its own index.

1. Enable **PC Indexing** (Settings, or the wizard step).
2. **Settings → PC Indexing → Add folder…** to add a folder to the Windows Search scope. The progress
   bar and status reflect Windows' own indexer; **Windows options…** opens the OS Indexing Options.
3. Semantic re-ranking is optional and uses the **Embeddings** model (a local Ollama
   `nomic-embed-text`); without it, results fall back to keyword order.

### 5.4 Hotkeys

The summon, writing-tools, and visual-intelligence shortcuts are all **rebindable**: open the
relevant Settings page, click **Change…**, and press a new combination (include a modifier). Hotkey
changes apply on the next launch.

---

## 6. Build a distributable installer

To ship WindowsAI to machines without the .NET SDK or the Store, build a self-contained, unpackaged
`.exe` installer. This needs [Inno Setup 6](https://jrsoftware.org/isdl.php) on the build machine.

```powershell
pwsh installer\build.ps1            # win-x64 (default); use -Rid win-arm64 for ARM64
```

`build.ps1` runs `dotnet publish` with `WindowsPackageType=None` + self-contained WindowsAppSDK (so
the app runs with no MSIX identity and no separately-installed runtime), then compiles
`installer\WindowsAI.iss`. The finished installer is written to `installer\output\`. It installs to
Program Files, adds Start Menu / optional desktop shortcuts, offers an opt-in "run at startup", and
its uninstaller **preserves** your `%LOCALAPPDATA%\WindowsAI` data. If WebView2 is missing it points
you to the free runtime. Code signing is recommended (to avoid SmartScreen) but optional.

---

## 7. Where your data lives

Everything is local to your Windows profile. Nothing is sent to a server unless you actively use a
cloud model or web search.

| Data | Location | Format and protection |
| :--- | :--- | :--- |
| Settings | `%LOCALAPPDATA%\WindowsAI\` | Plain JSON |
| Conversations | `%LOCALAPPDATA%\WindowsAI\conversations\` | One JSON file per conversation, auto-titled |
| Secrets (keys, tokens) | `%LOCALAPPDATA%\WindowsAI\secrets\` | DPAPI-encrypted, per-user |
| Crash log | `%TEMP%\WindowsAI-crash.log` | Appended plain text |

To reset the app completely, quit it from the tray and delete the `%LOCALAPPDATA%\WindowsAI` folder.

---

## 8. Troubleshooting

### The app crashes immediately with `REGDB_E_CLASSNOTREG`

You launched the built `.exe` directly. WinUI 3 needs package identity. Start the app with
`dotnet run --project src\WindowsAI.App\WindowsAI.App.csproj` instead.

### Build fails with `NETSDK1047` (assets file missing a target)

The platform was not specified, so the build picked the wrong RID. Always pass the platform:

```powershell
dotnet build src\WindowsAI.App\WindowsAI.App.csproj -c Debug -p:Platform=x64
```

This also applies to building the solution file directly — specify `-p:Platform=x64`.

### Local chat does nothing / "could not reach the model"

The Ollama server is not running or is on a different address.

- Run `ollama list` to confirm the server responds and the model is pulled.
- Ollama usually auto-starts at login; if not, launch the Ollama app or run `ollama serve`.
- Check the endpoint in **Settings → Models & providers → Local model** matches your server
  (default `http://localhost:11434`).

### "Search my PC" finds nothing or is not re-ranked

- Local retrieval queries the Windows Search index — confirm Windows Search is enabled and your
  files are in an indexed location (your user profile is indexed by default).
- Semantic re-ranking needs the embedding model: `ollama pull nomic-embed-text`. Without it, results
  fall back to keyword order (still functional).

### Gemini 3.x model returns 404 on Vertex

The region is wrong. Set **Vertex AI region** to **`global`** — the 3.x models are only served there.

### Google sign-in fails or loops

- Confirm the OAuth client is a **Desktop app** type, not Web.
- Add your Google account as a **test user** on the OAuth consent screen.
- Confirm the **Vertex AI API** is enabled on the project you entered.

### A provider is added but not used

Adding a provider does not switch to it. Open the **Models** page and select its model to make it the
active chat model (and set per-feature models under Settings). If a card shows "Add an API key" or
"Sign in to connect", it is not yet usable — use **Edit** / **Test** on the card to finish setup.

### Hotkeys do nothing

- Another app may already own that global chord. Confirm the app is running (tray icon present).
- `Ctrl + Shift + Space` (writing tools) and `Alt + Shift + S` (capture) act on the foreground app's
  selection or screen — make sure the target app, not WindowsAI, had focus when you pressed them.

### Diagnosing an unexpected crash

The app routes fatal exceptions to `%TEMP%\WindowsAI-crash.log` (it deliberately fails fast rather
than masking unknown state). Open that file for the stack trace.

---

<div align="center">

Back to the [README](../README.md) · See the [Architecture guide](ARCHITECTURE.md)

</div>
