# One ADO ticket to a verified fix - Copilot SDK + Foundry

A deliberately small **.NET 10** demo, designed for **9 minutes plus 1 minute
buffer**: read one Azure DevOps **User Story**, post one business question, read
the human answer from its Acceptance Criteria on the next invocation, then
approve and capture a real failing baseline. Separately approve creation of a
linked **Bug**, let a Copilot SDK worker repair the local candidate, verify 12
protected checks, and approve publication and optional demo-only resolution.
The same controller can run as a **Microsoft
Foundry hosted agent**.

The ticket is the input; an explicit human rounding answer and approval are
required on every run. No autonomous PR or code push.

## Workflow

1. **Requirement:** "Calculate compensation for eligible delay claims."
2. **Clarification:** CHF 10.03 at 25% is 250.75 rappen. The PO chooses down,
   up or nearest **in the User Story AC**; `check` verifies the answer from ADO.
   The application never guesses or pre-fills approval.
3. **Investigation:** the SDK sees the ticket, actual failing results and policy
   source, not the faulty line or replacement. It explains and proposes a repair.
4. **Evidence:** real compilation, red-to-green results and a reviewable candidate.
5. **ADO + Foundry:** separately approved linked Bug, publication and optional
   demo-only Bug resolution; the controller runs
   through Foundry's Invocations protocol when deployed before the session.

Only a copied workspace contains the single seeded bug. Repository claims code
stays correct. Validation, rounding, ledger and acceptance tests are protected.
The editable surface is a small delay-band expression grammar, not arbitrary C#.
This is a synthetic money example, not legal or passenger-rights guidance.

## Setup

```bash
export PATH="$HOME/.dotnet:$PATH" # only if needed
dotnet restore Demo.slnx --locked-mode
dotnet build Demo.slnx
dotnet test tests/Demo.Tests/Demo.Tests.csproj
```

The pinned .NET SDK is in [global.json](global.json). Set live configuration
using [.env.example](.env.example) as a reference; the application reads process
environment variables and **does not auto-load dotenv files**. Never commit
secrets. The explicit `create-story` command below creates one synthetic Agile
**User Story** from [demo/ticket.json](demo/ticket.json), rounding unresolved.
It is setup, not part of repair; it does nothing until you execute and approve it.
The presenter must confirm the live ticket matches that bounded contract and
the existing acceptance runner. Agile `User Story`, `Bug`, `New` and `Resolved`
must be supported by the project; other process contracts are not translated.

```bash
export ADO_ORGANIZATION="swissreghcpsdkdemo"
export ADO_PROJECT="CoP-agentic-sdlc-demo"
# Set ADO_PAT (Work Items read/write) or ADO_TOKEN securely.
export DEMO_MODEL="<model-or-deployment>"
# Use Copilot login/COPILOT_GITHUB_TOKEN, or configure BYOK:
export DEMO_MODEL_ENDPOINT="https://<account>.openai.azure.com/openai/v1"
export DEMO_PROVIDER="openai"
export DEMO_WIRE_API="completions"
# Set DEMO_MODEL_API_KEY or DEMO_MODEL_BEARER_TOKEN securely.
```

Missing ADO configuration fails explicitly; it never silently substitutes a
fixture. If inference uses Copilot login instead of BYOK, leave all
`DEMO_MODEL_ENDPOINT`/provider/key settings unset.

## Live commands

**No initial story was created by this implementation.** Remote approval was
declined; no ADO resources were read, created, changed or deleted for validation.
Run setup yourself only when authorized:

```bash
dotnet run --no-build --project agent/Demo.Agent -- create-story live1 --by presenter --approve-create
# The output gives the NEW User Story ID and URL. ADO_WORK_ITEM_ID is unnecessary:
# the journal pins this run to the created story and exact organization/project.
dotnet run --no-build --project agent/Demo.Agent -- init live1
dotnet run --no-build --project agent/Demo.Agent -- ask live1 --by presenter
# STOP: the actual PO edits FR-004 in the User Story Acceptance Criteria in ADO.
# Replace that clause with exactly ONE of:
# FR-004: Rounding: down
# FR-004: Rounding: up
# FR-004: Rounding: nearest
# Do not copy a choice automatically. Preserve FR-001..003 and FR-005..007.
dotnet run --no-build --project agent/Demo.Agent -- check live1
# Review the refreshed story/decision. check never approves anything.
dotnet run --no-build --project agent/Demo.Agent -- approve live1 --by presenter
dotnet run --no-build --project agent/Demo.Agent -- test live1
# Review real baseline failures before approving this distinct remote write:
dotnet run --no-build --project agent/Demo.Agent -- open-bug live1 --by presenter --approve-create-bug
dotnet run --no-build --project agent/Demo.Agent -- execute live1 --mode sdk
dotnet run --no-build --project agent/Demo.Agent -- export live1 --by reviewer
# Review .demo/runs/live1/export/ before approving publication:
dotnet run --no-build --project agent/Demo.Agent -- publish live1 --by reviewer
# OPTIONAL: explicitly acknowledge this synthetic Bug is resolved ONLY for
# a verified local candidate, not integrated/deployed production code:
dotnet run --no-build --project agent/Demo.Agent -- resolve-bug live1 --by reviewer --ack-local-candidate
```

The User Story remains authoritative; a linked Bug is created **only after
actual failing tests**, never as the requirement input. `test` and `execute`
perform no remote writes. No Bug is auto-created by execution. `check` can run
in another process; local `clarify` is **offline-only**. An unresolved, duplicate
or conflicting FR-004 remains blocked. The canonical clause means integer
rappen; `nearest` uses half-up ties. Comments do not count as an answer.
The demo's `test` command succeeds when it captures the **expected failed**
baseline; its report still says `passed: false`. Unexpected green or a runner/
compilation error fails the command and cannot open a Bug.

`approve`, `test`, `execute` and `publish` refresh live ADO. Changed requirement
content invalidates earlier approvals and verification, restores the seeded
workspace and requires review again. Revision-only changes retain approvals
but refresh the write guard. Publication and resolution use revision tests;
a race or ambiguous PATCH retry fails, rather than silently overwriting.
Inspect History after an uncertain write; do not blindly repeat it. Resolution
also checks the published requirement snapshot and unchanged candidate/evidence.
It records `Resolved` on the linked synthetic Bug, **not** the User Story.
The state/History explicitly say the fix is only a local candidate. Omit
resolution if your process requires integration/deployment before that state.
Presenter labels are self-asserted, not authenticated enterprise approvals.

Inspect `.demo/runs/live1/export/review.md` and `CompensationPolicy.cs`.
The application does not commit or push the code. The worker gets 10 tool calls,
2 repair verifications and a 2-minute SDK turn; total execution is capped at
3 minutes. Failure stays failure; there is no automatic offline fallback.

## Clean reset (new story ID)

Only when authorized to delete **this tracked demo's items**:

```bash
dotnet run --no-build --project agent/Demo.Agent -- reset live1 --by presenter --approve-reset
dotnet run --no-build --project agent/Demo.Agent -- init live1
```

This soft-deletes the journal's explicitly tracked demo-created Bugs, then its
User Story, clears this run's local workspace/evidence/approvals, and creates the
original unresolved User Story with a **new ID**, no old comments or linked Bugs.
ADO cannot clear history on the same ID. Normal deletion moves old items to the
**recycle bin**, not permanent erasure; this application never requests permanent
deletion. Retention/audit history may remain. Permanent deletion requires a
separate authorization and is deliberately not implemented.

Reset verifies the persisted organization/project, exact IDs, work item types
and generation-specific ownership tags before deletion. It never searches for
unrelated items to delete; in particular existing **Bug 1 is not managed**.
Do not change/remove ownership tags or edit/copy journals. ADO DELETE has no
revision-test operation: ownership is re-read immediately before soft deletion;
coordinate with other editors during reset.

The allow-list/checkpoint is `.demo/runs/live1/managed.json`; keep it secure and
durable, including across command failures. Partial deletions are saved one at a
time. Retry `reset` after fixing a deletion failure; an uncertain DELETE checks
the recycle bin and ownership before continuing. Creation first records a unique
marker, then reconciles an uncertain response by scoped read-only WIQL. It never
blindly repeats an uncertain POST. If the marker is not query-visible, it stops;
inspect ADO, wait for indexing, then retry the same action. A definite ADO create
rejection (such as a permissions/validation error) is reported distinctly; fix
the cause and retry the same action. Transport failures and server errors stay
uncertain and cannot cause another POST until the original is reconciled.
If recreation fails after cleanup, retry **`create-story live1 --by presenter
--approve-create`**, not a new reset. Never discard the journal to bypass this
guard. A lost journal/container prevents safe managed recovery/reset; do not
guess IDs or delete an existing ticket. See [walkthrough](demo/STEPS.md).

## Foundry infrastructure and deployment

- [Azure infrastructure with `azd up`](infra/README.md): resource group, Foundry
  account/project, exact `gpt-5.4-mini` deployment and an ADO project.
  Organization creation is manual. Model access remains Entra-only; the
  postdeploy hook builds and deploys the single-session controller to Foundry.
- [Deployment](deploy/README.md): official .NET Invocations host, Docker image,
  secret connection references, explicit deploy approval and active-status poll.
  Provision/build/deploy before running the short workflow.

## Explicit offline rehearsal

Use `init-offline <fresh-id>`, skip `ask`/`publish`, and execute with
`--mode rehearsal`. This uses a local ADO-shaped fixture and scripted selection
of a bounded patch, but real compilation/acceptance tests. It is always labelled
offline. Evidence is written to the run's local export directory; it does not
establish live ADO connectivity, model inference or cloud deployment.

Tests exercise the actual SDK runtime with a **local scripted provider**, not a
real model. They also use fake ADO HTTP responses, not a real organization.
Foundry hosting code and deployment assets are included; successful cloud
deployment, live ADO connectivity and real-model behavior must be verified with
your configured resources. Local passing tests do not establish any of those.
