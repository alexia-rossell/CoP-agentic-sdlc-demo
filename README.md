# Agentic SDLC Demo

A small TypeScript proof of concept using the **GitHub Copilot SDK** to turn a
natural-language software requirement into a local engineering backlog. The
model chooses tools; the application validates and persists their results.

This is standalone: no meeting data, customer information, other demos, Swiss Re
systems, Azure infrastructure or real Azure DevOps project is involved.
The Copilot model service requires a network connection; ticket storage is local.

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
demo-inputs/                 Complete and incomplete synthetic requirements
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

No Azure credentials, production endpoints or `.env` file are required.

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
   and Tasks using successful parent IDs returned by the tools.
4. **Review:** an explicit second turn checks security, testing, observability,
   operational readiness and acceptance criteria, and calls tools to fill gaps.
   Nonessential design decisions can be documented as demo assumptions.
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
- Add tracing through `AgentResult.calls` or a dedicated telemetry adapter;
  keep sensitive document content out of logs.
- Add application authentication, durable audit events or deployment adapters
  outside the backlog-planning core when moving beyond a demonstration.

There is currently no Azure infrastructure, AKS, Container Apps, Foundry
registration, Application Insights, production authentication or agent-to-agent
communication.
