<div align="center">

# WindowsAI.Smoke

A headless console runner that exercises the Core paths the UI depends on, end to end,
against real services.

![Target](https://img.shields.io/badge/net10.0--windows-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)
![Kind](https://img.shields.io/badge/Smoke%20runner-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)

[![Home](https://img.shields.io/badge/%E2%86%90%20%20README-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)](../../README.md)
&nbsp;
[![Architecture](https://img.shields.io/badge/Architecture-0B0E14?style=for-the-badge&labelColor=0B0E14&color=B06BFF)](../../docs/ARCHITECTURE.md)

</div>

---

## Responsibility

Where [the unit tests](../../tests/WindowsAI.Core.Tests/README.md) cover pure logic in isolation,
this runner wires up the real DI container and drives the integrated paths the UI relies on — using
the live Windows Search index, a local Ollama server, and the actual stores. It is the fastest way
to confirm the engine works without launching the WinUI app.

It references both `WindowsAI.Core` and `WindowsAI.Services`.

---

## What it checks

- Settings round-trip (save and reload).
- Conversation persistence: save, list, get, delete.
- Active-model persistence and the `Changed` event, including a no-op on an unchanged value.
- Windows Search retrieval with embedding re-rank.
- Model catalog discovery across local and cloud.
- Live local streaming through `ChatOrchestrator`, confirming reasoning traces do not leak.
- The agent tool-calling loop end to end: a scripted stub client invokes a real tool with the right
  arguments and the final reply reflects the tool result.

---

## Prerequisites

For the full set of checks to pass, a local Ollama server should be running with a chat model and
`nomic-embed-text` available:

```powershell
ollama pull nomic-embed-text
ollama pull gemma3        # or any chat model
```

Checks that need a service which is not available are reported as such rather than failing the run.

---

## Running

```powershell
dotnet run --project tools\WindowsAI.Smoke
```

The runner prints a pass/fail line per check and a final tally.
