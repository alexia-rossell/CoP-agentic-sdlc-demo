# One ticket to a verified fix — 10-minute demo

**One .NET Copilot SDK agent hosted in Foundry. Three steps.**
The presenter uses a .NET remote client; the agent reads/writes ADO, calls
`gpt-5.4-mini`, and compiles/tests the candidate **in Foundry**, not locally.

| Time | Step | What the audience sees |
|---|---|---|
| 0–3 min | **1. Ask** | The agent finds a missing business decision and asks in ADO. |
| 3–7 min | **2. Clarify and repair** | A human answers; the agent checks, reproduces and fixes the defect. |
| 7–9 min | **3. Review and publish** | Verified evidence is reviewed, then published to ADO. |

Keep the last minute as buffer. This is synthetic compensation logic, not
passenger-rights guidance. Only a private workspace copy contains the seeded bug.

## Before the audience arrives

Authenticate, build the client and deploy **before** the ten-minute demo.
See [deployment setup](deploy/README.md) and [Azure prerequisites](infra/README.md).
`azd up` includes the hosted agent and Application Insights configuration;
approve its prompts. Updating creates a new session, so do not redeploy mid-demo.

If the system `dotnet` is broken on this machine:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
hash -r
```

```bash
dotnet build Demo.slnx
```

Define this shortcut in the presenter's terminal. **Every `demo` command below
invokes the deployed Foundry agent**; it does not run the local workflow.

```bash
demo() { dotnet run --no-build --project agent/Demo.Agent -- remote "$@"; }
```

Create the initial unresolved User Story, with explicit permission:

```bash
demo create-story live1 --by presenter --approve-create
```

Open the returned Story URL in ADO. Keep its Acceptance Criteria and the terminal
visible. Use a fresh run ID, or use the reset command below for a previously
managed run. The client uses the existing deployment/session record and obtains
fresh Entra tokens; no manual PAT or model-key export is needed.

## Step 1 — Ask (3 minutes)

```bash
demo init live1
demo ask live1 --by presenter
```

**Show ADO:** the requirement says 25% compensation from 60 minutes and 50% from
120 minutes. The agent asks:

> CHF 10.03 × 25% = 250.75 rappen. Round down, up, or nearest with half-up ties?

**Point:** the agent asks instead of inventing a monetary rule. Posting the
question is approved, but implementation is not.

## Step 2 — Clarify and repair (4 minutes)

**Human action:** obtain the actual PO decision. In the Story's Acceptance
Criteria, replace only FR-004 with exactly one actual answer:
`FR-004: Rounding: down`, `FR-004: Rounding: up`, or
`FR-004: Rounding: nearest`. Preserve the other criteria; comments do not count.

```bash
demo check live1
```

**Show:** the new invocation reads the answer and ADO revision. It does not
infer approval. If no clear answer exists, stop: the run remains blocked.

After reviewing the answer and scope:

```bash
demo approve live1 --by presenter
demo test live1
```

**Show:** `BaselineFailed` and `before.passed: false` from real acceptance tests.
After reviewing those failures, explicitly authorize the linked Bug and repair:

```bash
demo open-bug live1 --by presenter --approve-create-bug
demo execute live1 --mode sdk
```

**Point:** the User Story stays the requirement; the linked Bug records a proven
defect. The model diagnoses from source/tests, makes a bounded edit, and verifies
it. Expected: passing candidate checks and `AwaitPublicationApproval`.
No arbitrary shell, test edits or autonomous ADO publication.

## Step 3 — Review and publish (2 minutes)

```bash
demo export live1 --by reviewer
```

**Review the response:** `candidatePolicy`, `review`, red-to-green results and
candidate hash. Evidence is returned from the remote workspace.

Only after reviewing it:

```bash
demo publish live1 --by reviewer
```

**Show ADO:** the Story now contains actual verification evidence.
If appropriate for this synthetic demo, separately acknowledge Bug resolution:

```bash
demo resolve-bug live1 --by reviewer --ack-local-candidate
```

This resolves only the **verified workspace candidate**, not integrated or
deployed production code. No commit, push, PR, merge or automatic Story closure.

**Close:** human decisions, bounded model repair, real tests and reviewed evidence.
If telemetry has been deployed and ingestion verified beforehand, show the same
run in **Foundry → Agents → Traces**; do not spend demo time troubleshooting it.

---

**Reset before another presentation** — deletes only this run's tracked demo
Story/Bugs and recreates a new unresolved Story ID:

```bash
demo reset live1 --by presenter --approve-reset
```

Then start at Step 1. ADO recycle-bin history is not permanently erased.
Unrelated items, including the original Bug 1, are untouched.

**Single-session demo:** container/session loss can lose journals. Never blindly
repeat create/reset/publication after an interrupted request; inspect ADO first.
No offline fallback is presented as a live model run.

Details: [hosting, tracing and cleanup](deploy/README.md) ·
[extended walkthrough](demo/STEPS.md) · [infrastructure](infra/README.md).
