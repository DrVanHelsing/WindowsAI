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

Verifies `Alt+Shift+S` capture → a real multimodal answer. Vision now uses **any configured vision
provider**, preferring the active one, else **Gemini → Vertex → OpenRouter**. With the user's
working Gemini key this is testable today.

1. Ensure a vision provider is configured (a **Gemini API key** in Settings is enough).
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
2. Settings → **Gemini via Google sign-in**: enter the **client id**, **client secret**, **project
   id**, and leave **region = `global`** (the 3.x models are only served there).
3. Click **Sign in** → the system browser opens for consent → approve.
   - [ ] Status flips to **"Signed in ✓"**.
4. Pick **`google/gemini-3.5-flash (Google sign-in)`** in the model picker and send a message.
   - [ ] A reply streams via Vertex.
   - [ ] **Sign out** forgets the session (status returns to "Ready to sign in").

---

## 4. Web search round-trip (Tavily)

Verifies the `search_web` agent tool and its step surfacing.

1. Create a free **Tavily API key** at [tavily.com](https://tavily.com) (starts with `tvly-`).
2. Settings → **Web search** → **Manage…**: paste the API key.
   - [ ] Status shows **"Configured ✓"**.
3. With a **cloud** model active, ask something current (e.g. "search the web for the latest .NET
   release").
   - [ ] A **"Searched the web · …"** step line appears above the reply.
   - [ ] Result **link chips** render and open in the browser when clicked.
   - [ ] The answer reflects the fetched results.
4. Also confirm **`get_current_datetime`**: ask "what's today's date?" → a "Checked the current
   time" step appears and the date is correct.
