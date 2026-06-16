# WindowsAI — Manual QA checklist

These are the sign-offs that need a human at the keyboard with the right apps and keys. Everything
below has hardened, compile-clean code paths; this checklist is the final click-through.

Launch the app with `dotnet run --project src/WindowsAI.App` (a bare `.exe` crashes with
`REGDB_E_CLASSNOTREG`, and the `.slnx` build can target x86 vs. run's x64).

---

## 1. Writing-tools paste injection

Verifies the type-anywhere flow: capture a selection via synthesized `Ctrl+C`, transform it, and
paste the result back over the selection in the originating app.

For **each** target app — **Notepad**, **WordPad / Word**, **Chrome (Gmail compose)**, **VS Code**:

1. Type/select a sentence with an obvious error (e.g. "teh quick brwon fox").
2. Press **`Ctrl+Shift+Space`** — the writing-tools card appears near the cursor.
3. Run each action in turn: **Proofread**, **Rewrite**, **Tone**, **Summarize**.
4. Click **Replace** and confirm:
   - [ ] The selection is replaced in-place (not appended, not pasted into the wrong window).
   - [ ] Focus returns to the originating app.
   - [ ] Your **previous clipboard contents are restored** (paste elsewhere to check).
   - [ ] **Copy** (instead of Replace) puts the result on the clipboard and dismisses the card.
5. Edge cases:
   - [ ] Trigger with **nothing selected** → no card, clipboard untouched.
   - [ ] Trigger over a **read-only** field → Replace can't paste; clipboard still restored, no crash.

> Hardening notes: `SendInput` now reports whether the OS accepted the copy/paste chord; Replace
> re-asserts foreground focus on the target window (one retry) and **only pastes once focus is
> confirmed**; the original clipboard is restored in a `finally` on every path. If an app needs a
> longer settle time, note it — the delays are `120 ms` (copy) and `60 ms` focus + `150 ms` (paste).

---

## 2. Visual-intelligence round-trip (screen capture → vision answer)

Verifies `Alt+Shift+S` capture → a real multimodal answer. Vision uses the **Visual Intelligence**
capability model (Settings → Visual Intelligence), or auto-picks the first configured vision-capable
provider when left unset. With any working vision provider (e.g. a Gemini key) this is testable today.

1. Ensure a vision-capable provider is configured (add a **Gemini** provider on the Models & providers
   tab), the **Visual Intelligence** feature is enabled, and (optionally) a vision model is picked under Settings → Visual Intelligence.
2. Press **`Alt+Shift+S`** and drag a region over:
   - [ ] A **text-heavy** area (e.g. a paragraph or code) — ask "what does this say?" → OCR-quality
     answer streams.
   - [ ] An **image / UI** area — ask "what's in this?" → a correct description streams.
3. Confirm:
   - [ ] The answer streams token-by-token into the capture window.
   - [ ] **Cancelling** mid-stream (close / Esc) stops cleanly.
   - [ ] With **no vision provider configured**, the answer area shows the clear
     "needs a vision-capable provider" message rather than a raw error.

---

## 3. Google sign-in round-trip (Vertex AI / OAuth)

Verifies the in-app OAuth consent (the API-key path to the same models is already live-verified).

1. In your Google Cloud project: enable the **Vertex AI API** and create an **OAuth "Desktop app"**
   client.
2. Models & providers → **+ Add provider** → preset **Gemini via Google sign-in**: enter the
   **client id**, **client secret**, **project id**, and leave **region = `global`** (the 3.x models
   are only served there). Save.
3. On the new provider card, click **Sign in** → the system browser opens for consent → approve.
   - [ ] The card's status dot turns connected.
4. Pick the Vertex model (`google/gemini-3.5-flash`) in **Models** and send a message.
   - [ ] A reply streams via Vertex.
   - [ ] **Remove** (or sign out) forgets the session.

---

## 4. Web search round-trip (Tavily)

Verifies the `search_web` agent tool and its step surfacing.

1. Create a free **Tavily API key** at [tavily.com](https://tavily.com) (starts with `tvly-`).
2. Settings → **Web Search**: enable the feature, then **Manage…** and paste the API key.
   - [ ] Status shows **"Key set ✓"**.
3. With a **cloud** model active, ask something current (e.g. "search the web for the latest .NET
   release").
   - [ ] A **"Searched the web · …"** step line appears above the reply.
   - [ ] Result **link chips** render and open in the browser when clicked.
   - [ ] The answer reflects the fetched results.
   - [ ] Turning **Web Search off** in Settings stops the tool being offered.
4. Also confirm **`get_current_datetime`**: ask "what's today's date?" → a "Checked the current
   time" step appears and the date is correct.

---

## 5. Configurable providers, onboarding & installer (Phase 8)

Verifies the data-driven provider rework, the first-run wizard, per-feature routing, and the installer.

**First-run wizard** — clear onboarding state (delete `%LOCALAPPDATA%\WindowsAI\settings.json` or set
`Onboarding.HasCompleted` to false) and relaunch:
- [ ] The setup wizard appears: welcome → connect → features → PC indexing → personalize → done.
- [ ] **Skip** works at any step; **Back/Next** navigate; the PC indexing step only shows when enabled.
- [ ] Completing it (or skipping) doesn't show the wizard again on the next launch.

**Add any provider** — Models & providers → **+ Add provider**:
- [ ] Picking a preset (e.g. **OpenAI**, **Groq**, **LM Studio**) pre-fills the base URL + models.
- [ ] **Test connection** reports a clear result (connected / key rejected / unreachable).
- [ ] **Fetch models** lists the provider's models; pick a default and **Add**.
- [ ] Add a fully **Custom (OpenAI-compatible)** endpoint and confirm it works the same way.
- [ ] The card shows status + enable toggle; **Edit / Remove** work; disabling hides its models.

**Per-feature routing & flags** (Settings):
- [ ] Assign **different** models to Chat vs Visual Intelligence vs Writing Tools; confirm each
  feature uses its assigned model.
- [ ] Toggle a feature off (e.g. Writing Tools) → its hotkey no longer fires; re-enable → works.
- [ ] **Change…** a hotkey (capture a new chord) → applies after the next launch.

**PC indexing** (Settings → PC Indexing):
- [ ] **Add folder…** adds it to the Windows index scope; status/progress updates; `search_my_pc`
  returns files from it. "Windows options…" opens the OS Indexing Options.

**Migration** — start with an existing pre-rework `settings.json`:
- [ ] Providers are seeded from the old fields and previously-stored API keys still work.

**Installer** — `pwsh installer\build.ps1` (with Inno Setup installed), then on a **clean** machine:
- [ ] The `.exe` installs; the app launches unpackaged (tray, Alt+Space, no `REGDB_E_CLASSNOTREG`).
- [ ] Uninstall removes the app but **preserves** `%LOCALAPPDATA%\WindowsAI` data.
