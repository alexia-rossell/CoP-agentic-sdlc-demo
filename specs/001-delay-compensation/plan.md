# Demo implementation plan: delay compensation

**Feature:** `001-delay-compensation` | **Git branch:** `ex_manolito`
**Date:** 2026-10-16 | **Input:** [Demo specification](spec.md)

## Two layers

The repository specification is intentionally unresolved presentation input.
Building the demo does not mean approving that fictional business contract.
Each run records its own rounding answer and approval in a private snapshot.

Use .NET 10 for both the claims target and the Copilot SDK console host.
The user explicitly removed Rust from the demo scope.

## Structure

```text
app/Claims.Service/       Integer policy and single-process claim ledger
app/Claims.Acceptance/    Independent executable acceptance suite
agent/Demo.Agent/               SDK host, bounded tools and durable workflow
tests/Demo.Tests/             Target, workflow and SDK integration tests
.demo/runs/<id>/              Generated snapshots, workspace and evidence (ignored)
```

## Decisions

- Pin .NET SDK, Copilot SDK/runtime and package lock files.
- Use signed integer rappen; fare range 0..1,000,000,000 and delay range
  0..1,000,000 whole minutes. Reject invalid JSON/numeric inputs explicitly.
- Serialise ledger writes with a lock. This is a single-process synthetic
  ledger, not durable payment storage or distributed idempotency.
- Commit a correct target library. Inject the labelled threshold/rounding
  defects only into a newly created per-run workspace.
- Require explicit rounding (`down`, `up`, `nearest` with half-up ties), then
  local presenter approval. Never resolve the repository draft automatically.
- Supply approved snapshots to the SDK, with the execution skill explicitly
  configured and included. Spec Kit's full interactive procedures are not
  automatically executed by this SDK demo.
- Give the model only `read_policy`, `replace_expression`, and `verify`.
  Limit patches to two reviewed replacements. No arbitrary code, paths,
  shell tools, contract edits or test edits.
- Compile/test through a fixed host command; validate protected-file hashes
  and candidate shape before and after. Bound calls, verification and duration.
- Persist application stages/approvals and actual tool/results separately from
  runtime session files. Each execution uses a fresh SDK session; this demo
  demonstrates application checkpoint recovery, not SDK conversation resume.
- Export a local evidence bundle only after separate approval.

## Workflow

Draft -> clarify -> approve -> real baseline failure -> bounded SDK repair ->
real candidate verification -> await export approval -> local export.

Offline rehearsal uses the same tools and real compilation/tests but scripted
tool selection. It is always labelled and never a fallback for failed SDK runs.
Run Spec Kit analysis/convergence separately when presenting those steps.

## Verification

| Requirement | Acceptance evidence |
|---|---|
| FR-001..003 | 0/59/60/119/120 boundaries |
| FR-004 | Explicit rounding; zero and maximum fare |
| FR-005 | Sequential/concurrent retries, one ledger entry |
| FR-006 | Conflicting reuse, original unchanged |
| FR-007 | Invalid ranges/ID/non-integer/missing fields |

Workflow tests cover approvals, tampering, patch limits, failure, recovery and
export. A local scripted provider exercises the actual SDK runtime/tool loop;
it does not validate any real model or cloud gateway.

## Boundaries

Local approvals are presenter-supplied labels, not authenticated enterprise
approvals. The workspace is not an OS/container sandbox; safety here comes from
reviewed exact patches and restricted tools, not arbitrary code isolation.

Live ADO publication, Foundry hosted deployment and cloud tracing are follow-on
integrations. Foundry remains a short platform note, never a claimed deployment
from a local test. No remote resources are created by this demo.
