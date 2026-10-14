# Agentic SDLC Demo

A small TypeScript proof of concept using the **GitHub Copilot SDK** to turn a
natural-language software requirement into a local engineering backlog. The
model chooses tools; the application validates and persists their results.

This is standalone: no meeting data, customer information, other demos, Swiss Re
systems or real Azure DevOps project is involved. The Copilot model service
requires a network connection; ticket storage is local to wherever the demo runs.

**Contents:** [Quick start](#quick-start) ·
[Demo scenario](#demo-scenario) · [Architecture](#architecture) ·
[Local web demo](#local-web-demo) · [Troubleshooting](#troubleshooting) ·
[Run in a container](#run-in-a-container) ·
[Deploy to Azure Container Apps](#deploy-to-azure-container-apps) ·
[Agentic loop](#how-the-agentic-loop-works) · [Tools](#available-tools) ·
[Evaluations](#evaluations) · [Future work](#future-extension-points)

## Quick start

```sh
npm ci
npm run demo:web     # open http://127.0.0.1:4173
```

Click **Load complete example**, then **Generate backlog**. Prefer the terminal?
Use `npm run demo -- complete`. Requires Node.js 22.12+ and a signed-in Copilot
account (see [Prerequisites](#prerequisites-and-installation)). If a run times
out, see [Troubleshooting](#troubleshooting).

## Demo scenario

> Build an internal claims-document processing application. Employees should be
> able to upload a claims document, extract relevant information, review and
> correct the extracted fields, and submit the result. Access must require
> organisational authentication and every change must be auditable.

The demo **plans** that application; it does not implement document processing,
production authentication or an audit service. Use only synthetic requirements,
never real claims documents, personal information or credentials.

## Architecture

```text
src/cli.ts                   CLI entry point
src/agent/                   Copilot sessions, prompts, assessment and review loop
src/tools/                   Four schema-validated agent tools
src/storage/                 TicketStore interface and JSON implementation
src/server/                  Local web API, run manager and demo validation
src/web/                     Browser UI (HTML, CSS, TypeScript, pure renderers)
src/telemetry/               Run event model recorded by agent and tools
demo-inputs/                  Complete and incomplete synthetic requirements
evaluations/                 Tool-selection dataset, live runner and offline tests
data/tickets.json            Runtime backlog (generated, gitignored)
```

The SDK runs in `empty` mode with an explicit allowlist of custom tools.
No shell, file-editing, external integration or agent-to-agent tools are exposed.
Only the local store implementation can write tickets.

## Prerequisites and installation

- Node.js **22.12 or later** and npm.
- A GitHub account entitled to use Copilot and access to the Copilot model service.
- Authenticate locally with GitHub Copilot CLI: run `copilot` and follow its login
  prompt (or `/login`). Install the CLI if needed following the
  [official instructions](https://docs.github.com/en/copilot/how-tos/copilot-cli/cli-getting-started).
  The SDK includes its own runtime; a separate CLI is useful for interactive login.
  The demo uses the standard `~/.copilot` directory, or `COPILOT_HOME` if set.
  Keep authentication outside this repository.

```sh
npm ci
npm run check
npm test
```

No Azure credentials, production endpoints or `.env` file are required locally.
In a container or other headless host, set `GH_TOKEN` instead of logging in.

## Local web demo

```sh
npm run demo:web      # builds, then serves http://127.0.0.1:4173
npm run test:web      # offline tests for the API, validation and UI rendering
```

A single-page UI over the same Copilot SDK workflow, laid out as requirement →
agentic reasoning → tool execution → backlog → validation:

- **What should we build?** requirement editor with incomplete/complete synthetic
  examples. It shrinks once a run starts so attention moves to execution.
- **Agentic loop:** an Understand → Plan → Act → Observe → Review indicator and a
  live timeline of real events only (phase, monospace tool name, ticket ID,
  actual duration, Created/Reused/Error). Nothing is inferred or invented.
- **Engineering backlog:** expandable Feature → User Story → Task tree with
  category badges (implementation, security, testing, observability, operations)
  and priority; incomplete requirements show a "Clarification required" panel
  with topic, question and reason instead.
- **Demo validation:** each structural check as PASS/FAIL plus an overall result;
  deterministic checks only, not a model-quality benchmark.
- **Presentation mode** (header toggle, frontend only) enlarges text and hides
  secondary controls for projected screens.

The UI supports keyboard navigation, visible focus, text-plus-colour status and
`prefers-reduced-motion`. Copilot login is required as for the CLI.

Options and behaviour:

- **Start each run from an empty backlog** (on by default) clears the web store
  before each run so IDs restart at `DEMO-100`. Untick it to keep and reuse
  existing tickets.
- **Expand all / Collapse all** fold or unfold every work item; the Feature starts
  open and stories start collapsed.
- **Reset demo** deletes only the web store and is refused while a run is active.
- Runs use a separate store, `data/web-demo-tickets.json` (override with
  `DEMO_STORE_PATH`, which must be a `.json` file inside `data/`).
- `PORT` changes the port; the server binds to localhost unless `HOST` is set
  (the container image sets `HOST=0.0.0.0`).
- Restart `npm run demo:web` after pulling or changing code.
- A complete run typically takes 1–2 minutes (planning is the slowest step).

Demo validation is deterministic and structural only (hierarchy, unique IDs,
task categories, IDs traceable to tool results); it is not a model-quality score.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `Timeout after 120000ms waiting for the structured response` | Copilot auth or runtime stalled. In the same terminal run `$env:GH_TOKEN = (gh auth token)` (PowerShell) or `export GH_TOKEN=$(gh auth token)`, then start the demo again. The variable is per terminal window. |
| `No GitHub OAuth token or Copilot HMAC key provided` in a container's `~/.copilot/session-state/*/events.jsonl` | The container has no `gh` login. Set `GH_TOKEN` (or `COPILOT_GITHUB_TOKEN`); the app passes it to the client and every session. |
| `Standalone managed telemetry preparation failed ... error sending request` | The image lacks system CA certificates, which the Copilot runtime's native HTTPS client needs. The `Dockerfile` installs `ca-certificates`; rebuild the image. |
| `Run not found` after a restart or redeploy | Run state is in memory and is lost on restart. Start a new run. |
| `copilot` or `gh` not recognised | Open a new terminal after installing so PATH refreshes. |
| `tsc` not found | Run `npm ci` first. |
| `Missing script: demo:web` | You are in an old copy of the repo; pull the latest `main`. |
| `Unrecognized key` in the UI | A stale server is still running; stop it and restart `npm run demo:web`, then hard-refresh. |
| Reset refused | A run is active or a `.lock` file exists; wait, or remove the lock only if no writer is running. |

## Run in a container

A multi-stage `Dockerfile` builds the web demo and runs it as the non-root `node`
user with production dependencies only.

```sh
docker build -t agentic-sdlc-demo .
docker run --rm -p 127.0.0.1:4173:4173 -e GH_TOKEN="$(gh auth token)" agentic-sdlc-demo
```

In PowerShell use `$env:GH_TOKEN = (gh auth token)` and then
`docker run --rm -p 127.0.0.1:4173:4173 -e GH_TOKEN agentic-sdlc-demo`.
Open http://127.0.0.1:4173.

- Authentication is supplied at run time through `GH_TOKEN`; never bake tokens
  into the image or commit them. The image contains no data or credentials.
- The ticket store lives in `/app/data` inside the container and is discarded
  with it. Mount a volume at `/app/data` to keep it.
- The demo API has no authentication of its own. Publish the port on
  `127.0.0.1` as shown; put it behind authenticated ingress before exposing it
  anywhere else.
- `GET /api/health` backs the image `HEALTHCHECK`.
- The token is read from `COPILOT_GITHUB_TOKEN`, `GH_TOKEN` or `GITHUB_TOKEN`
  (in that order) and passed explicitly because containers have no `gh` login.
- If a corporate proxy breaks `npm ci` during a local `docker build`, build in
  Azure instead (see below).

## Deploy to Azure Container Apps

The container runs unchanged on Azure Container Apps. The steps below are the
ones used for the demo deployment; replace the placeholder names. Builds run in
Azure (`az acr build`), so no local Docker is needed.

```powershell
$rg = "rg-agentic-sdlc-demo"; $acr = "<unique-acr-name>"; $loc = "westeurope"

az group create -n $rg -l $loc
az acr create -g $rg -n $acr --sku Basic
az acr build -r $acr -t agentic-sdlc-demo:latest .      # run from the repo root

az containerapp env create -g $rg -n cae-agentic-sdlc -l $loc
az containerapp create -g $rg -n agentic-sdlc-demo --environment cae-agentic-sdlc `
  --image "$acr.azurecr.io/agentic-sdlc-demo:latest" --registry-server "$acr.azurecr.io" `
  --registry-identity system --system-assigned `
  --target-port 4173 --ingress external --min-replicas 1 --max-replicas 1 `
  --cpu 1 --memory 2Gi `
  --secrets gh-token="$(gh auth token)" --env-vars GH_TOKEN=secretref:gh-token
```

To redeploy after a change, rebuild and create a new revision (the suffix must
be new each time):

```powershell
az acr build -r $acr -t agentic-sdlc-demo:latest .
az containerapp update -g $rg -n agentic-sdlc-demo `
  --image "$acr.azurecr.io/agentic-sdlc-demo:latest" --revision-suffix v2
```

Operating notes:

- **Access control:** the demo API has no authentication of its own. Enable
  Container Apps authentication with Microsoft Entra ID
  (`az containerapp auth microsoft update` and `az containerapp auth update`
  with redirect to login) before sharing the URL. Some tenants limit client
  secret lifetime; pass a shorter `--end-date` if creation is rejected.
- **Token:** the secret holds a GitHub token for an account with Copilot access.
  Use a short-lived or dedicated token and rotate it after the demo.
- **State:** run state and the ticket store are in memory and on the container
  filesystem. Keep a single replica, and do not redeploy just before presenting;
  a restart clears the backlog.
- **Debugging:** `az containerapp logs show -g $rg -n agentic-sdlc-demo --tail 60`
  and `az containerapp exec ... --command sh`; Copilot session logs are in
  `/home/node/.copilot/session-state/<id>/events.jsonl`.
- **Cost and cleanup:** scale to zero replicas when idle, or remove everything
  with `az group delete -n $rg --yes`.

## Run the demo

Run these commands from the repository root:

```sh
npm run demo -- incomplete
npm run demo -- complete
npm run demo -- --request "Your synthetic software requirement"
npm run demo -- --help
```

`complete` is the default. Optional environment settings:

- `COPILOT_MODEL`: a model available to your Copilot account; otherwise the SDK
  selects its default.
- `DEMO_STORE_PATH`: another local JSON file (default `data/tickets.json`).
  Keep any custom runtime output outside version control.

The scripts compile TypeScript into the gitignored `dist/` directory. To start
over, remove `data/tickets.json` while the demo is stopped. The next ID is
`DEMO-100`. Each successful new write advances the ID; reuse does not.

## How the agentic loop works

1. **Assess:** a read-only Copilot session analyses the requirement. Only
   `request_missing_information` is available. The model returns a typed
   readiness decision.
2. **Clarify or proceed:** if essential details are missing, the tool returns
   structured questions (`topic`, `question`, `reason`) and the run ends without
   creating tickets. An incomplete decision without questions fails closed.
   Existing tickets are left untouched.
3. **Plan and act:** for a sufficient request, a second session receives the
   requirement and existing local backlog. It creates a Feature, User Stories
   and Tasks using successful parent IDs returned by the tools in that run.
   Existing ancestors must be resolved through their creation tools first;
   matching items are reused, not duplicated.
4. **Review:** an explicit second turn checks security, testing, observability,
   operational readiness and acceptance criteria, and calls tools to fill gaps.
   Nonessential design decisions can be documented as demo assumptions.
   Missing security, testing, observability or operations task categories cause
   an explicit failure rather than a success summary.
5. **Report:** the host builds a concise hierarchy from successful tool results,
   not the model's prose. Created and reused items are both included.

Tools are validated at runtime. The store checks parent types, serializes writes
within a store instance, uses an exclusive writer lock and atomically replaces
the JSON file. Another process attempting a concurrent write fails rather than
overwriting data. A crash can leave a `.lock` file: remove it only after verifying
that no writer is running. Corrupt stores are rejected, not silently reset.
Runs are not whole-backlog transactions: successful writes before an error stay
on disk. Inspect the store after a failed run.

## Available tools

| Tool | Inputs / result |
| --- | --- |
| `request_missing_information` | List of questions with topic and reason; returns structured clarification without tickets |
| `create_feature` | Title, description, `businessValue`, tags; returns Feature ID |
| `create_user_story` | Feature `parentId`, title, persona, goal, benefit, nonempty `acceptanceCriteria`; returns User Story ID |
| `create_engineering_task` | User Story `parentId`, title, description, category, priority; returns Task ID |

Task categories: `implementation`, `security`, `testing`, `observability`,
`operations`. Priorities: `high`, `medium`, `low`. Stored items include `id` and
`type`; successful creation tools also return `created` (`false` means reused).

Deduplication matches type, parent and normalized title (case and whitespace
insensitive), including across restarts. The agent sees existing tickets and is
instructed to reuse stable titles. This is deliberately simple: semantic
duplicates with different titles can still occur. Reuse does not update an
existing item's fields.

## Expected outputs

For `incomplete` (“Build an AI application for claims.”), expect questions about
users, workflow and essential constraints:

```text
Clarification required; no tickets created.
- Who uses the application and what should it do?
...
```

For `complete`, expect a Feature → User Story → Task hierarchy. Illustrative
output from an empty store (titles and counts vary with the model):

```text
Backlog (created or reused by successful tools):
DEMO-100 Feature: Claims document processing
  DEMO-101 User Story: Review and submit a claim
    DEMO-102 Task: Implement document upload validation
    DEMO-103 Task: Plan organisational access control and audit history
    DEMO-104 Task: Test extraction, correction and submission
    DEMO-105 Task: Add privacy-safe logs and operational metrics
    DEMO-106 Task: Document recovery and operational readiness
```

Acceptance criteria and full descriptions are in the JSON store. The agent never
gets a successful ticket result until persistence succeeds, and the CLI never
prints invented ticket IDs.

## Evaluations

```sh
npm test          # deterministic, offline tests; no Copilot account required
npm run evaluate # live Copilot tool-selection evaluation; requires login/network
```

`evaluations/cases.json` specifies required/forbidden tools, expected status and
task categories for both demo inputs. Live evaluations run each case against a
fresh temporary store, print actual handler calls and return a nonzero exit code
on failure. They do not modify `data/tickets.json`. Model behavior is
nondeterministic; a successful offline test run is not a live tool-selection
score.

Offline tests exercise real tools and storage with scripted SDK session
responses: clarification gating, valid parents, sequential IDs, deduplication,
restart persistence, corrupt data, writer contention, invalid inputs, failed
tool writes, review turns, timeouts and summaries that ignore fabricated IDs.

## Future extension points

- Implement `TicketStore` with an Azure DevOps adapter later, preserving tool
  schemas and the agent loop. No real project or credentials are used now.
- Extend prompts, schemas and evaluation cases for richer acceptance criteria,
  semantic deduplication, update operations or approval steps.
- Extend the existing run events (`src/telemetry/`) or `AgentResult.calls` into
  OpenTelemetry tracing;
  keep sensitive document content out of logs.
- Add application authentication, durable audit events or deployment adapters
  outside the backlog-planning core when moving beyond a demonstration.

Not yet implemented: OpenTelemetry export or Application Insights, Foundry
Control Plane registration, durable storage for the hosted demo, production
authentication beyond the Entra sign-in in front of the demo, and agent-to-agent
communication.
