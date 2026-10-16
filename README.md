# Spec-to-change demo: .NET + Copilot SDK

A small, runnable SDD walkthrough: clarify a fictional compensation rule, approve
the run's contract, repair a prepared boundary defect, verify it, then export
reviewable evidence. Both the target and agent application use **.NET 10**.

Spec Kit provides the preparation skills/artifacts. Copilot SDK supplies the
live model/tool loop. The application owns approvals, scoped tools and state.

## Run locally

Requires .NET SDK **10.0.402** (or a later patch in that SDK feature band).
On this Ubuntu machine it is installed in `~/.dotnet`.

```bash
export PATH="$HOME/.dotnet:$HOME/.local/bin:$PATH"
dotnet restore Demo.slnx --locked-mode
dotnet build Demo.slnx --no-restore
dotnet test tests/Demo.Tests/Demo.Tests.csproj --no-restore
```

The first build acquires the SDK package's pinned Copilot CLI runtime from
GitHub and verifies its checksum. Restore/build require network access initially.

### Offline rehearsal: no model or credentials

```bash
dotnet run --project agent/Demo.Agent -- init first-demo
dotnet run --project agent/Demo.Agent -- clarify first-demo --rounding down
dotnet run --project agent/Demo.Agent -- approve first-demo --by presenter
dotnet run --project agent/Demo.Agent -- execute first-demo --mode rehearsal
dotnet run --project agent/Demo.Agent -- status first-demo
dotnet run --project agent/Demo.Agent -- export first-demo --by reviewer
```

Expect real failing baseline tests, real passing candidate tests, and an evidence
bundle in `.demo/runs/first-demo/export/`. Each command is a separate process:
the checkpoint survives without publishing anything. Use a fresh ID to start over.

**Rehearsal is explicitly scripted tool selection, not AI inference.** It uses
the same restricted tools and actual .NET compilation/acceptance runner as SDK mode.

### Live Copilot SDK

Use a fresh run, clarify and approve it, then execute with `--mode sdk`.
Set `DEMO_MODEL` explicitly to a model/deployment available to your account.

For Copilot-backed inference, provide your entitled account's token through
`COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, or `GITHUB_TOKEN`. Keep it outside this repository.
The runtime has a separate per-run data directory, so do not assume it sees your
usual interactive CLI login.

For BYOK through a compatible gateway or provider, configure:

```bash
export DEMO_MODEL="your-model-or-deployment"
export DEMO_MODEL_ENDPOINT="https://your-gateway.example/openai/v1"
export DEMO_PROVIDER="openai"      # openai, azure, anthropic
export DEMO_WIRE_API="completions" # or responses, if supported by your endpoint
# Set DEMO_MODEL_API_KEY or DEMO_MODEL_BEARER_TOKEN securely in your environment.
```

Then run `execute <run-id> --mode sdk`. Provider settings must match the actual
endpoint's path, protocol and authentication; do not assume every gateway works.
BYOK inference avoids Copilot authentication, not the Copilot runtime dependency.
No real-model/cloud endpoint has been verified by the local tests.

## What to show

- [Agenda](DEMO_AGENDA.md) and [presenter steps](demo/STEPS.md).
- [Unresolved demo spec](specs/001-delay-compensation/spec.md), constitution,
  plan and tasks. The repository draft deliberately stays unresolved.
- Per-run resolved spec, approval hash and protected acceptance suite.
- Real red-to-green boundary checks and two allowlisted code-expression patches.
- Durable stages, explicit failures and separately approved local export.

The model has only `read_policy`, `replace_expression`, and `verify`. It receives
the approved artifact snapshots and an explicitly loaded execution skill.
This is bounded patch execution, not arbitrary code generation or a full
automatic invocation of Spec Kit's interactive lifecycle.

## Tests and boundaries

`dotnet test` covers exact money, all rounding choices, concurrent duplicate
handling, invalid inputs, approval/tampering gates, budgets, recovery and export.
It also exercises the **real SDK/CLI runtime** against a local scripted
OpenAI-compatible provider. That test requires no external inference credentials
and is not a model-quality evaluation.

The ledger is single-process/in-memory. Approval identities are local labels.
Run files are not tamper-proof audit storage. The private workspace is not an
OS sandbox; only reviewed exact patches can be compiled through the tools.
Do not extend this to arbitrary model-generated code without actual isolation.

**Not implemented:** live ADO publication, MCP integration, Foundry hosting,
cloud tracing or production payments. No push, PR, merge or deployment occurs.
Foundry is a short future platform integration, not the focus of the demo.

Spec Kit **1.2.0** is installed locally at `~/.local/bin/specify`; Copilot-only
skills are checked into `.github/skills/`. Run `specify version` and
`.specify/scripts/bash/check-prerequisites.sh --json --require-spec --require-tasks`
to verify setup. See the [official SDK](https://github.com/github/copilot-sdk)
and [Spec Kit](https://github.com/github/spec-kit) documentation.
