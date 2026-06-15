<div align="center">

# WindowsAI.Core.Tests

xUnit unit tests over the platform-agnostic core.

![Target](https://img.shields.io/badge/net10.0-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)
![Framework](https://img.shields.io/badge/xUnit-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)

[![Home](https://img.shields.io/badge/%E2%86%90%20%20README-0B0E14?style=for-the-badge&labelColor=0B0E14&color=6E7BFF)](../../README.md)
&nbsp;
[![Core](https://img.shields.io/badge/Core%20under%20test-0B0E14?style=for-the-badge&labelColor=0B0E14&color=B06BFF)](../../src/WindowsAI.Core/README.md)

</div>

---

## Responsibility

Fast, deterministic unit tests over pure logic in `WindowsAI.Core`. These run with no network, no
local model, and no Windows UI, so they are safe in any CI environment. Anything that needs a live
model or the Windows Search index belongs in the [smoke runner](../../tools/WindowsAI.Smoke/README.md)
instead.

---

## Coverage

| Area | What is verified |
| :--- | :--- |
| Writing action catalog | The declarative `WritingAction.All` set is well-formed. |
| Prompt shape | `WritingPrompt` produces the expected instructions for each action. |
| Result cleaning | `CleanResult` strips stray quotes and code fences without mangling content. |
| Web search parsing | The Tavily Search result parser maps responses to result objects. |

The `Writing/` and `Tools/` folders mirror the structure of the code under test.

---

## Running

```powershell
dotnet test tests\WindowsAI.Core.Tests
```

The project is not packable (`IsPackable=false`) and references only `WindowsAI.Core`.
