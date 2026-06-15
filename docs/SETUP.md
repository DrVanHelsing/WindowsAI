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
3. [First run](#3-first-run)
4. [Configure models and providers](#4-configure-models-and-providers)
   - [Local model (Ollama)](#41-local-model-ollama)
   - [OpenRouter](#42-openrouter)
   - [Gemini (API key)](#43-gemini-api-key)
   - [Gemini via Google sign-in (Vertex AI, OAuth)](#44-gemini-via-google-sign-in-vertex-ai-oauth)
   - [Vision model](#45-vision-model)
   - [Web search](#46-web-search)
5. [Where your data lives](#5-where-your-data-lives)
6. [Troubleshooting](#6-troubleshooting)

> **You do not need any of the cloud providers.** A local Ollama model alone powers chat,
> retrieval, and writing tools with no network and no keys. Sections 4.2 onward are optional
> upgrades — set up only the ones you want.

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

## 3. First run

1. The app launches to the **tray**. Find its icon in the system tray (the up-arrow overflow if
   hidden).
2. Press the global hotkeys from anywhere:

   | Shortcut | Action |
   | :--- | :--- |
   | `Alt + Space` | Summon or dismiss the overlay |
   | `Ctrl + Shift + Space` | Writing tools over the current text selection |
   | `Alt + Shift + S` | Capture a screen region and ask about it |
   | `Esc` | Dismiss the overlay or any floating surface |

3. Open the full app from the tray menu and go to **Settings** to configure models. With no setup,
   the app expects a local Ollama server (next section).

---

## 4. Configure models and providers

All configuration is in the app under **Settings → Models & providers** (and **Settings → Tools**
for web search). API keys and OAuth tokens are encrypted with Windows DPAPI and never leave this PC.
The active model is shared across the overlay, the chat page, and the models page.

### 4.1 Local model (Ollama)

The default, fully offline path.

1. Install Ollama from [ollama.com/download](https://ollama.com/download).
2. Pull a chat model and the embedding model used for local-file re-ranking:

   ```powershell
   ollama pull gemma3            # any chat model works; pick to taste
   ollama pull nomic-embed-text  # enables semantic re-rank for "Search my PC"
   ```

3. Confirm the server is running — Ollama normally runs as a background service at
   `http://localhost:11434`. Test it:

   ```powershell
   ollama list
   ```

4. In **Settings → Models & providers → Local model (Ollama)**, the endpoint defaults to
   `http://localhost:11434`. Use **Change…** only if your server runs elsewhere.
5. Open **Models** (in the app shell) and select your pulled model. You are ready to chat offline.

> If a model rejects tool-calling (some local models do), the app automatically falls back to a
> plain reply — agent tools simply stay inactive for that model.

### 4.2 OpenRouter

A single key that reaches many cloud models (and the default vision backend).

1. Create an account at [openrouter.ai](https://openrouter.ai) and generate an API key under
   **Keys**.
2. In **Settings → Models & providers → OpenRouter (cloud)**, click **Manage…** and paste the key.
   The status dot turns to the accent color when configured.
3. The key is stored encrypted (DPAPI) on this PC only. Use **Remove** to delete it.
4. Select an OpenRouter model from the **Models** page to make it active.

### 4.3 Gemini (API key)

Google Gemini through a simple AI Studio key.

1. Get a key from [Google AI Studio](https://aistudio.google.com/apikey).
2. In **Settings → Models & providers → Gemini (cloud)**, click **Manage…** and paste the key.
3. Stored encrypted (DPAPI), local only. Use **Remove** to delete it.
4. Select a Gemini model from the **Models** page.

> Prefer signing in with Google instead of pasting a key? Use the Vertex AI path in [4.4](#44-gemini-via-google-sign-in-vertex-ai-oauth).

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

**In WindowsAI — Settings → Models & providers → Gemini via Google sign-in:**

5. **OAuth client id** → **Change…** → paste the client ID.
6. **Client secret** → **Set…** → paste the secret (stored encrypted via DPAPI).
7. **Google Cloud project id** → **Change…** → enter your project ID.
8. **Vertex AI region** → **Change…** → set to **`global`**. The Gemini 3.x generation is only
   served on `global`; regional endpoints such as `us-central1` return 404 for those models.
9. Click **Sign in…** and complete the browser consent. The status updates when connected.
10. Select a Vertex model on the **Models** page. Defaults are **`google/gemini-3.5-flash`** with
    **`google/gemini-3.1-pro-preview`** also available.

> Use **Sign out** to revoke the stored token at any time.

### 4.5 Vision model

Visual intelligence (`Alt + Shift + S`) always needs a multimodal model, even when your everyday
chat model is local text-only. The app prefers the active provider when it is multimodal, otherwise
the first configured vision provider in the order Gemini, then Vertex, then OpenRouter.

To set the OpenRouter vision model explicitly: **Settings → Models & providers → OpenRouter →
Vision model → Change…**. The default is a capable free multimodal all-rounder; switch to a faster
OCR-focused model for document-heavy regions.

### 4.6 Web search

Lets the assistant search the web via [Tavily](https://tavily.com) (the agent `search_web` tool).

1. Sign up at [tavily.com](https://tavily.com) and create an **API key** (the free tier is enough
   for occasional agent use; keys start with `tvly-`).
2. In **Settings → Tools → Web search** → **Manage…** → paste the API key (encrypted via DPAPI).
3. Ask a cloud model something current; the `search_web` step and result link chips appear above the
   reply when the tool is used.

---

## 5. Where your data lives

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

## 6. Troubleshooting

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

### A cloud model is configured but not used

Configuring a provider does not switch to it. Open the **Models** page and select the model to make
it the active one. The active model is shared across every surface.

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
