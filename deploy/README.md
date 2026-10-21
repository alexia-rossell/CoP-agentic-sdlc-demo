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
build, creates a presenter-secret connection, deploys the controller, and records
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

This creates billable registry/build/hosted resources. An active recorded
deployment is reused. An incomplete record stops retries: inspect ACR/Foundry
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

You issue commands, inspect JSON state/evidence, and use ADO for the PO decision.
This is **not chat in the Foundry playground**.

```bash
# Optional hosting-only smoke test: not ADO or real-model evidence.
dotnet run --project agent/Demo.Agent -- remote init-offline cloud-smoke
dotnet run --no-build --project agent/Demo.Agent -- remote status cloud-smoke

# Explicitly approved live setup.
dotnet run --no-build --project agent/Demo.Agent -- remote create-story live1 --by presenter --approve-create
dotnet run --no-build --project agent/Demo.Agent -- remote init live1
dotnet run --no-build --project agent/Demo.Agent -- remote ask live1 --by presenter
```

The response provides the story URL through its events. Open it in ADO. The
product owner edits FR-004 Acceptance Criteria to exactly one actual decision:
`FR-004: Rounding: down`, `FR-004: Rounding: up`, or
`FR-004: Rounding: nearest`. Preserve the other requirements. No automatic choice
or answer in a comment is treated as approval.

```bash
# A later request reads ADO and checks whether the human clarified it.
dotnet run --no-build --project agent/Demo.Agent -- remote check live1
dotnet run --no-build --project agent/Demo.Agent -- remote approve live1 --by presenter
dotnet run --no-build --project agent/Demo.Agent -- remote test live1
dotnet run --no-build --project agent/Demo.Agent -- remote open-bug live1 --by presenter --approve-create-bug
dotnet run --no-build --project agent/Demo.Agent -- remote execute live1 --mode sdk
dotnet run --no-build --project agent/Demo.Agent -- remote export live1 --by reviewer
# Review candidatePolicy, review and test evidence in the export response first.
dotnet run --no-build --project agent/Demo.Agent -- remote publish live1 --by reviewer
# Optional acknowledgement: only a verified synthetic local candidate.
dotnet run --no-build --project agent/Demo.Agent -- remote resolve-bug live1 --by reviewer --ack-local-candidate
```

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

## Validation and cleanup

```bash
dotnet build Demo.slnx
dotnet test tests/Demo.Tests/Demo.Tests.csproj
az bicep build --file infra/hosted.bicep --stdout > /tmp/hosted-template.json
```

To stop costs, review and delete the hosted agent and its sessions in the Foundry
portal. The local record contains its exact name and endpoint. Removing the agent
does not remove ADO items. `azd down` removes Azure resources in its resource group,
including the registry; review the complete deletion scope first.

Reference: [Hosted agent deployment](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/deploy-hosted-agent)
and [session routing](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/manage-hosted-sessions).
