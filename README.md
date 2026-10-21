# One ticket to a verified fix

**Five-minute demo. Three steps. One .NET agent running in Foundry.**

Before presenting, complete [setup](deploy/README.md), build the client, and
define this remote-only shortcut. Rebuild after pulling changes: `--no-build`
otherwise runs the old client.

```bash
dotnet build Demo.slnx
demo() { dotnet run --no-build --project agent/Demo.Agent -- remote "$@"; }
```

## Presenter flow: three commands, two human pauses

`start` and `fix` compose the existing hosted operations in the .NET client.
All ticket access, approvals, tests and SDK execution still run in Foundry.
No agent redeployment is required to use these client-side commands with the
existing hosted operations. Detailed commands remain available for diagnostics.

Use a fresh run ID, such as `live3`. If a prior session was lost or redeployed,
inspect existing ADO items before creating another Story. No automatic reset.

## 1. Agent asks — 1 minute

```bash
demo start live3 --approve-create --by presenter
```

Create the Story, initialize its private workspace, and post the clarification
question. Return the ADO link and show:
**250.75 rappen — round down, up or nearest?**

**Human pause 1:** the PO makes the business decision. The agent waits instead
of guessing. Story creation and posting the question require explicit approval.

## 2. Human clarifies; agent fixes — 3 minutes

The PO updates FR-004 in the Story's Acceptance Criteria to their actual choice:
`FR-004: Rounding: down`, `FR-004: Rounding: up`, or
`FR-004: Rounding: nearest`.

```bash
demo fix live3 --by presenter --approve-create-bug
```

Read the saved answer from ADO, approve the ticket/test scope, run and display
the real failing baseline, create the linked Bug under the explicit approval,
then let the Copilot SDK diagnose, repair and verify the bounded policy
expression. Return the candidate diff and actual test results for review.

If the answer is missing or ambiguous, stop with
**"Waiting for the PO's rounding decision"** rather than an internal-stage error.
Do not create a Bug or begin repair until the clarification and baseline checks
have succeeded. A build/startup error is not a failing acceptance baseline.

## 3. Review and publish — 1 minute

**Human pause 2:** review the returned diff and passing tests. Only then approve
publication:

```bash
demo publish live3 --by reviewer
```

Publish the reviewed evidence to the ADO Story **and a findings comment on the
linked Bug**, with revision and ownership checks. The Bug comment includes the
actual failures, protected before/after source diff, SDK attribution, model
explanation (labelled as review text), passing checks and candidate hash.
The **Proposed code fix - CompensationPolicy.Calculate** section preserves the
diff's line breaks in a code block, so the code work is visible directly in ADO.
A changed ticket, Bug or candidate stops publication; no old approval is reused.
The Bug remains **New**: publishing findings is not resolving or deploying a fix.

**Human decision → verified fix → reviewed evidence.**
No code push, merge or production deployment.

## What to show

`start`, `fix` and `publish` now return readable summaries instead of the entire
checkpoint history: Story/Bug links, actual failing assertions, the SDK's code
change, protected verification results and the delivery boundary. Append `--json`
for the complete state, candidate source and review document:

```bash
demo fix live3 --by presenter --approve-create-bug --json
```

Do not rerun a completed repair just to change output format. Use
`demo status live3` to inspect its saved state.

New SDK runs also record provider-reported input, output and cache token counts,
and a trace ID to find the model/tool loop in **Foundry > Agents > Traces** or
Application Insights. Missing usage is shown as unavailable, not zero.
The SDK's `Cost` is a Copilot billing multiplier, **not USD**; it is not used to
price Azure BYOK inference. Monetary costs must be checked in Azure billing.
See [tracing and token queries](deploy/README.md#tracing).

Rebuilding the client gives readable output against older hosted versions, but
does not invent missing diffs or usage. **Bug findings, structured diffs and SDK
telemetry require deploying the updated controller.** Deploy before the demo,
not mid-run: a new session does not migrate current journals.

## Internal checkpoints are not presentation steps

Keep `init`, `ask`, `check`, `approve`, `test`, `open-bug`, `execute` and `export`
available for diagnostics and recovery. The presenter commands compose
those operations without removing approval gates, protected tests, tool budgets
or revision checks. The presenter should not have to manage each internal
transition during the short demo.

`fix` displays the actual failed checks before invoking the SDK and returns
the exported candidate and verification evidence. It does not publish to ADO.
If a command fails, inspect `demo status <run-id>` before retrying; an interrupted
or failed SDK run is not automatically retried. Repeating `start` does not reset
an existing workspace.
