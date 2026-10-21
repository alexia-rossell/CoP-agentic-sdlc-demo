# One simple controller hosted in Foundry

The same .NET Copilot SDK controller runs in Foundry using the official
`Azure.AI.AgentServer.Invocations` library. There is no second agent, chat router,
custom database, or natural-language command parser. The presenter uses a thin
.NET CLI client; **all workflow execution happens in Foundry**.
Python remains only in the provisioning scripts, not the client or agent.

## Deploy

Sign into Azure CLI and azd with the intended account/tenant. Provision the base
infrastructure using [azd setup](../infra/README.md), then:

```bash
azd up
```

The postdeploy hook asks for explicit deployment approval. It creates a Basic
ACR, grants the Foundry project's identity image-pull access, runs a cloud image
build, provisions workspace-based Application Insights and its project connection,
creates a presenter-secret connection, deploys the controller, and records
one Foundry session. No local Docker daemon or jq is needed.

If the base infrastructure already exists, the same deployment is available via:

```bash
python3 deploy/provision.py --approve
```

To explicitly build and deploy a new version of the recorded agent:

```bash
python3 deploy/provision.py --approve --update
```

This starts a new session; do not use it mid-demo expecting journals to migrate.

This creates billable registry/build/hosted/telemetry resources. The bare
provisioning command reuses an active recorded deployment; `azd up` uses
`--update` to deploy the current code/configuration with approval. An incomplete
record stops retries: inspect ACR/Foundry
before repairing it. Do not discard the record to work around an ambiguous create.
The script never creates or modifies ADO work items.

The generated presenter secret is in ignored `.azure/hosted-approval-token`
(mode 0600); the endpoint/session record is `.azure/hosted-demo.json`.
Do not commit/share either file. The image excludes `.azure` and dotenv files.

## Authentication without model keys

The `remote` command uses `az login` credentials to obtain new Entra tokens for **each
invocation**:

- Foundry audience: authenticate the remote request.
- Azure DevOps audience: pass the presenter's identity to that command only.
- Cognitive Services audience: only for `execute --mode sdk`.

Tokens use Foundry's forwarded `X-Client-*` HTTPS request headers, not shell arguments, saved environment values,
model prompts, journal fields or image configuration. The controller does not
mutate process-global credentials. Model bearer tokens are passed directly to
the SDK's existing BYOK provider. Each repair is bounded to two minutes; this is
not a background service that needs an extra token-refresh daemon.

The caller needs Foundry invocation access, model inference access and the
appropriate ADO project permissions. The additional presenter secret gates every
invocation; each remote-write command still requires its explicit action flags.
Model API-key authentication remains disabled. No ADO PAT is required.

## What the interaction looks like

You issue commands, review readable evidence, and use ADO for the PO decision.
This is **not chat in the Foundry playground**.

```bash
# Optional hosting-only smoke test: not ADO or real-model evidence.
dotnet run --project agent/Demo.Agent -- remote init-offline cloud-smoke
dotnet run --no-build --project agent/Demo.Agent -- remote status cloud-smoke

# Explicitly approved live setup: create Story, initialize and post the question.
dotnet run --no-build --project agent/Demo.Agent -- remote start live3 --approve-create --by presenter
```

The response shows the Story URL. Open it in ADO. The
product owner edits FR-004 Acceptance Criteria to exactly one actual decision:
`FR-004: Rounding: down`, `FR-004: Rounding: up`, or
`FR-004: Rounding: nearest`. Preserve the other requirements. No automatic choice
or answer in a comment is treated as approval.

```bash
# Read the answer, approve scope, show the baseline, open Bug, repair and export.
dotnet run --no-build --project agent/Demo.Agent -- remote fix live3 --by presenter --approve-create-bug
# Review the actual diff, failing baseline and protected passing checks first.
dotnet run --no-build --project agent/Demo.Agent -- remote publish live3 --by reviewer
# Optional acknowledgement: only a verified synthetic local candidate.
dotnet run --no-build --project agent/Demo.Agent -- remote resolve-bug live3 --by reviewer --ack-local-candidate
```

Build the updated client first (`dotnet build Demo.slnx`). `start` and `fix` are
client-side orchestration over existing hosted operations, so they do not require
redeployment. Missing clarification returns a waiting message without approval
or Bug creation. The individual `create-story`, `init`, `ask`, `check`, `approve`,
`test`, `open-bug`, `execute` and `export` commands remain available for diagnosis.
Append `--json` for the full state instead of the presenter summary.
The updated controller is required for structured before/after diffs, SDK usage
and Bug findings. An older controller is explicitly labelled when evidence is
missing; rebuilding the client does not update the deployed controller.

`publish` checks Story and Bug revisions/ownership, then adds findings to Bug
History and the Story: real failures, source diff, SDK/rehearsal attribution,
labelled model explanation, passing checks, hash and trace ID. It never resolves
the Bug or pushes the candidate. A successful Bug comment is checkpointed before
the Story update, so a definitive Story-update failure can resume without a
duplicate Bug comment. A stale revision or ambiguous lost response stops rather
than retrying a write blindly. Previously published Story evidence is not duplicated
when explicitly publishing missing Bug findings in the same preserved session.

Reset from the client, not the local controller:

```bash
dotnet run --no-build --project agent/Demo.Agent -- remote reset live1 --by presenter --approve-reset
dotnet run --no-build --project agent/Demo.Agent -- remote init live1
```

It deletes only tracked ownership-verified demo items and recreates a new
unresolved story. Existing unrelated Bug 1 is untouched. ADO recycle-bin history
is not permanently erased.

## Deliberate single-session limitation

The client passes the recorded `agent_session_id` in every invocation's query
string, keeping commands in the same Foundry sandbox. Do not delete the session
record or run parallel presenter workflows.

The controller currently writes checkpoints under `/app/.demo`, **not** Foundry's
persisted `$HOME`. Container replacement, session expiry or infrastructure reset
can lose journals/evidence. Foundry's native session capabilities do not make
arbitrary container paths durable. This deliberately simple demo therefore makes
**no restart recovery guarantee**. Never recreate a managed journal or repeat
setup/reset blindly after a lost response/restart: inspect ADO first.

The SDK compiles protected acceptance tests inside the image; the .NET SDK and
bundled Copilot CLI are intentionally present. No tools permitting arbitrary
shell commands, contract edits or remote writes are exposed to the model.
The SDK selection in `global.json` permits .NET 10 feature-band roll-forward:
local 10.0.113 and the published 10.0.402 SDK container can both run this project.
Acceptance subprocesses use a writable per-run `runtime/dotnet-home` for `HOME`,
`DOTNET_CLI_HOME` and the NuGet package cache. They do not rely on permissions
on the platform-injected `/home/session`, and they do not change the host's
process-wide environment. CLI caches are outside the protected workspace.

### Acceptance runner startup failures

`test` must return a structured `state.before` report before a Bug can be opened.
An "Acceptance runner did not complete" error instead means compilation/startup
failed or produced no valid report; it is not the intended red baseline.
The error response includes the last 6,000 characters of process output, and
the full output stays in the session's `baseline-output.log`. Check that output
for SDK resolution, restore, permissions or compiler errors before retrying.
An older deployed image may return only the log filename. Updating local source
does not change that running image. Deploying a new version starts a new session,
so inspect existing ADO items first and do not reset/recreate a live run blindly.

## Tracing

The pinned AgentServer host already includes OpenTelemetry and an Azure Monitor
exporter; no second telemetry SDK or exporter is configured. The `AppInsights`
Foundry project connection lets the platform inject
`APPLICATIONINSIGHTS_CONNECTION_STRING` into a new agent session.

The host traces requests and dependencies. The application adds correlated
`demo.command`, `sdk.repair`, `sdk.read_policy`, `sdk.replace_expression` and
`sdk.verify` spans. Actual SDK `AssistantUsageEvent` events add correlated GenAI
client spans (`chat <model>`) with operation/provider/model metadata and reported
input/output/cache tokens. Tool spans carry their tool names; the repair span
and model calls carry the synthetic run ID. The saved run includes the trace ID
and deduplicated per-call usage, including usage received before a failed repair.
Context-window occupancy events are not counted as consumed tokens.

Only safe operation/stage/error-type, synthetic run/model and numeric usage
metadata is emitted, not ticket text, source, tool arguments/results, credentials
or model messages.
GenAI content capture is disabled. Platform logs/access controls still need
independent review.

Use Foundry **Agents > Traces** or the connected Application Insights resource.
The deployment record includes `applicationInsightsId`. The Log Analytics
workspace uses 30-day retention and a 1 GB/day ingestion cap, which is not a
monetary spending limit. In Application Insights Logs, use:

```kusto
union requests, dependencies
| where timestamp > ago(30m)
| where cloud_RoleName == "demo-agent"
| project timestamp, name, operation_Id, success, duration, customDimensions
| order by timestamp desc
```

Filter by the exact SDK trace ID printed by `fix`:

```kusto
union requests, dependencies
| where timestamp > ago(1h)
| where operation_Id == "<trace-id-from-fix>"
| project timestamp, name, operation_Id, operation_ParentId, duration, customDimensions
| order by timestamp asc
```

For reported model tokens per run (Application Insights resource Logs):

```kusto
dependencies
| where timestamp > ago(1h)
| where cloud_RoleName == "demo-agent"
| where tostring(customDimensions["gen_ai.operation.name"]) == "chat"
| extend run = tostring(customDimensions["demo.run_id"]),
         model = tostring(customDimensions["gen_ai.response.model"]),
         input = todouble(customDimensions["gen_ai.usage.input_tokens"]),
         output = todouble(customDimensions["gen_ai.usage.output_tokens"]),
         cacheRead = todouble(customDimensions["gen_ai.usage.cache_read.input_tokens"])
| summarize modelCalls=count(), reportedInput=sum(input), reportedOutput=sum(output),
            reportedCacheRead=sum(cacheRead), callsWithInput=countif(isnotnull(input)),
            callsWithOutput=countif(isnotnull(output)) by run, model, operation_Id
```

Check `callsWithInput`/`callsWithOutput` against `modelCalls`: absent or partial
provider usage is **unavailable**, not evidence of zero tokens. The saved run
returns a null total for any token category missing on a reported call. Cache
counts are reported separately; do not add them to input tokens as a second charge.
Sampling, ingestion delays/caps or lost sessions can make portal totals incomplete.

**Costs:** SDK `AssistantUsageData.Cost` is a Copilot billing multiplier, not a
currency value. It is labelled as such only for Copilot-managed inference and
excluded for Azure BYOK. This demo does not guess dollar prices, claim `$0`, or
equate tokens to invoices. Use Azure Cost Management for actual Azure charges
(model inference, hosting, builds, registry and telemetry).

Local integration tests use the real SDK runtime with a scripted local provider
and an in-process activity listener. They verify exact reported token totals,
deduplication and content-free trace correlation, **not** real-model inference,
actual Azure prices or Foundry ingestion. Deployment is separate approval;
local code changes/tests do not update the running Foundry controller.

## Validation and cleanup

```bash
dotnet build Demo.slnx
dotnet test tests/Demo.Tests/Demo.Tests.csproj
python3 -B -m unittest discover -s deploy -p 'test_*.py'
az bicep build --file infra/hosted.bicep --stdout > /tmp/hosted-template.json
```

To stop costs, review and delete the hosted agent and its sessions in the Foundry
portal. The local record contains its exact name and endpoint. Removing the agent
does not remove ADO items. `azd down` removes Azure resources in its resource group,
including the registry; review the complete deletion scope first.

Reference: [Hosted agent deployment](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/deploy-hosted-agent)
and [session routing](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/manage-hosted-sessions).
