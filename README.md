# One ticket to a verified fix

**Five-minute demo. Three steps. One .NET agent running in Foundry.**

Before presenting, complete [setup](deploy/README.md), build the client, and
create the unresolved Story with `remote create-story live1 --by presenter
--approve-create`. Use a fresh run or [reset the previous one](deploy/README.md#what-the-interaction-looks-like).
Define this remote-only shortcut:

```bash
demo() { dotnet run --no-build --project agent/Demo.Agent -- remote "$@"; }
```

## 1. Agent asks — 1 minute

```bash
demo init live1
demo ask live1 --by presenter
```

Show the question in ADO: **250.75 rappen — round down, up or nearest?**
The agent waits instead of guessing.

## 2. Human clarifies; agent fixes — 3 minutes

The PO updates FR-004 in the Story's Acceptance Criteria to their actual choice:
`FR-004: Rounding: down`, `FR-004: Rounding: up`, or
`FR-004: Rounding: nearest`.

```bash
demo check live1
demo approve live1 --by presenter
demo test live1
```

Show the real failing tests. Review them, then authorize the linked Bug:

```bash
demo open-bug live1 --by presenter --approve-create-bug
demo execute live1 --mode sdk
```

The agent repairs the bounded policy expression and verifies the candidate.

## 3. Review and publish — 1 minute

```bash
demo export live1 --by reviewer
```

Review the returned diff and passing tests, then:

```bash
demo publish live1 --by reviewer
```

Show the evidence on the ADO Story. **Human decision → verified fix → reviewed
evidence.** No code push, merge or production deployment.
