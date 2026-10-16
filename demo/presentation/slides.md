<!-- .slide: id="start" -->

Scenario & Roles

## Fix two defects in a .NET delay-compensation calculation.

**Business rule:** 0% under 60 min · 25% at 60–119 min · 50% at 120+ min.  
**Open question (FR-004):** CHF 10.03 × 25% = 250.75 rappen — round `down`, `up`, or `nearest`?

| Actor / Component | Where it comes from | What it does & why |
|---|---|---|
| **Product Owner (ADO ticket)** | Human team / Azure Boards | Provides the raw ticket (*"25% at 60m, 50% at 120m"*), answers rounding (`down`), approves spec |
| **Spec Kit** (`.github/skills/speckit-*`) | Interactive IDE skills | Converts raw ADO ticket → `spec.md`, flags missing rules, generates `plan.md` & `tasks.md` |
| **Demo controller** (`agent/Demo.Agent`) | Custom .NET CLI host | Isolates a run workspace, records the PO's answer, locks spec/test hashes, gates export |
| **Copilot SDK worker** (`SdkWorker.cs`) | `GitHub.Copilot.SDK` in .NET | Reads `CompensationPolicy.cs`, applies the `>= 60` and rounding fixes, and runs the test check via 3 C# tools |
| **Target app & runner** (`app/`) | .NET claims service & tests | `CompensationPolicy.cs` + 12 acceptance checks in `Claims.Acceptance` |

[Draft spec](source.html?file=specs/001-delay-compensation/spec.md#line-45) · [Target policy](source.html?file=app/Claims.Service/CompensationPolicy.cs) · [Acceptance suite](source.html?file=app/Claims.Acceptance/Program.cs#line-37)

Note:
Start by explaining the concrete scenario and who does what.
1. The Product Owner opens a rough ADO work item: "Pay 25% compensation at 60–119 min and 50% at 120+ min."
2. Why don't we just feed that raw ticket straight to a coding agent? Because CHF 10.03 × 25% = 250.75 rappen — how to round fractional rappen is a business policy choice, not a coding decision.
3. Walk through the five rows in the table: PO provides the raw ADO ticket and answers business questions; Spec Kit turns that rough input into structured markdown artifacts in git; our .NET controller locks the approved contract; and the Copilot SDK worker executes the bounded code repair against the target .NET app.

---

<!-- .slide: id="spec-kit" -->

Step 1 / Developer in the IDE (`specs/001-delay-compensation/`)

## Run `/speckit-specify` and `/speckit-clarify` in the IDE.

**1. `/speckit-specify` converts the PO ticket into `spec.md` and flags the missing rule:**

<!-- excerpt: ../../specs/001-delay-compensation/spec.md -->
```markdown
- **FR-004:** Monetary inputs/results MUST be integer CHF rappen.
  [NEEDS CLARIFICATION: how should fractional-rappen compensation be rounded?]
```

**2. Developer runs `/speckit-clarify` in IDE chat → expected output to the PO:**

```text
> /speckit-clarify Focus on FR-004 fractional-rappen rounding.
Q1 (FR-004 — Rounding): CHF 10.03 × 25% = 250.75 rappen. How should fractional rappen be rounded?
  A) Round down (floor to 250 rappen)   B) Round up (251)   C) Round nearest half-up (251)
```

**3. After PO answers `A (down)`:** `/speckit-plan` & `/speckit-tasks` generate `plan.md` and `tasks.md`.

[Constitution](source.html?file=.specify/memory/constitution.md) · [Specify skill](source.html?file=.github/skills/speckit-specify/SKILL.md#line-107) · [Clarify skill](source.html?file=.github/skills/speckit-clarify/SKILL.md#line-65) · [Draft spec (FR-004)](source.html?file=specs/001-delay-compensation/spec.md#line-45) · [Plan](source.html?file=specs/001-delay-compensation/plan.md) · [Tasks](source.html?file=specs/001-delay-compensation/tasks.md)

Note:
Show the developer experience in the IDE before running any `.NET` CLI commands:
1. The developer feeds the rough ADO ticket into `/speckit-specify`. Guided by `.specify/memory/constitution.md`, it generates `spec.md` and writes `[NEEDS CLARIFICATION: how should fractional-rappen compensation be rounded?]` on `FR-004`.
2. Next, the developer runs `/speckit-clarify`. Show the expected output right on the slide: it scans `spec.md` and asks a concrete multiple-choice question (A: down to 250, B: up to 251, C: nearest to 251).
3. Once the PO answers `down`, `/speckit-plan` and `/speckit-tasks` map the spec to `CompensationPolicy.cs` and `Claims.Acceptance`.

---

<!-- .slide: id="controller" -->

Step 2 / Prepare the run (`agent/Demo.Agent` — pure C#, no AI/skills)

## Isolate a sandbox, record `down`, and lock SHA-256 hashes.

**Commands we run:**

```bash
dotnet run --project agent/Demo.Agent -- init my-demo
dotnet run --project agent/Demo.Agent -- clarify my-demo --rounding down
dotnet run --project agent/Demo.Agent -- approve my-demo --by presenter
```

**Captured CLI output (`01-init.log` → `02-clarify.log` → `03-approve.log`):**

<!-- excerpt: captured/01-init.log -->
```text
[Draft] init: Prepared two labelled fixture defects in a private run workspace; repository source unchanged.
```
<!-- excerpt: captured/02-clarify.log -->
```text
[AwaitSpecApproval] clarify: Recorded explicit 'down' decision in this run's artifact snapshot.
```
<!-- excerpt: captured/03-approve.log -->
```text
[Approved] approve: Local presenter 'presenter' approved this demo baseline and acceptance runner.
```

[Controller workflow (`Workflow.cs`)](source.html?file=agent/Demo.Agent/Workflow.cs#line-23) · [Resolved run spec](source.html?file=demo/presentation/captured/resolved-spec.md#line-45) · [Init log](captured/01-init.log) · [Clarify log](captured/02-clarify.log) · [Approve log](captured/03-approve.log)

Note:
Explain the commands and exact output of Step 2:
1. No AI or skills run here — this is deterministic C# in `Workflow.cs`.
2. `init` copies `specs/001-delay-compensation/*` and `app/*` into `.demo/runs/my-demo/` and seeds the two defects (`delayMinutes > 60` and hardcoded nearest rounding) into the copied `CompensationPolicy.cs`.
3. `clarify --rounding down` stamps the PO's `down` decision into `.demo/runs/my-demo/artifacts/spec.md` and `decision.json`.
4. `approve --by presenter` computes a SHA-256 hash over the spec and `Claims.Acceptance` test files so the AI worker in Step 3 cannot tamper with tests.

---

<!-- .slide: id="sdk" -->

Step 3 / Configure `CopilotClient` (`SdkWorker.cs`)

## Register the 3 C# tools and load the `demo-policy-change` skill.

**1. Bind C# methods as AI tools (`AIFunctionFactory` in `SdkWorker.cs`):**

<!-- excerpt: ../../agent/Demo.Agent/SdkWorker.cs -->
```csharp
AIFunctionFactory.Create(
    () => tools.ReadPolicy(),
    "read_policy", "Read the only editable policy source file."),
AIFunctionFactory.Create(
    ([Description("Exact existing expression, matching once.")] string original,
     [Description("Exact reviewed replacement supplied in the task.")] string replacement) =>
        tools.ReplaceExpression(original, replacement),
    "replace_expression", "Apply only one of the two reviewed patches. No paths, arbitrary code or test edits."),
AIFunctionFactory.Create(
    async () => await tools.Verify(),
    "verify", "Compile the candidate and run the protected acceptance suite; returns real results.")
```

**2. Create the session (`CopilotClientMode.Empty` + `SkillDirectories`):**

<!-- excerpt: ../../agent/Demo.Agent/SdkWorker.cs -->
```csharp
Model = settings.Model,
Provider = settings.Provider,
Tools = [.. functions],
AvailableTools = functions.Select(function => $"custom:{function.Name}").ToList(),
SkillDirectories = [tools.SkillDirectory],
```

[Tool binding (`SdkWorker.cs:54`)](source.html?file=agent/Demo.Agent/SdkWorker.cs#line-54) · [Session setup (`SdkWorker.cs:84`)](source.html?file=agent/Demo.Agent/SdkWorker.cs#line-84) · [Prompt (`SdkWorker.cs:123`)](source.html?file=agent/Demo.Agent/SdkWorker.cs#line-123) · [Execution skill](source.html?file=.github/skills/demo-policy-change/SKILL.md)

Note:
Show the two pieces of C# code in `SdkWorker.cs` that wire up the GitHub Copilot SDK:
1. Top block (`SdkWorker.cs` lines 54–67): `AIFunctionFactory.Create` wraps three C# methods on `WorkspaceTools` (`ReadPolicy`, `ReplaceExpression`, `Verify`) as tools the model can invoke.
2. Bottom block (`SdkWorker.cs` lines 84–91): `client.CreateSessionAsync` passes `Tools`, restricts `AvailableTools` to only those three custom tools, sets `Provider` (enabling BYOK / AI Gateway / Foundry routing), and points `SkillDirectories` to `.github/skills/demo-policy-change` (not `speckit-*`).

---

<!-- .slide: id="tools" -->

Step 3b / Inside the C# tools (`WorkspaceTools.cs`)

## How `read_policy`, `replace_expression`, and `verify` enforce boundaries.

<!-- excerpt: ../../agent/Demo.Agent/WorkspaceTools.cs -->
```csharp
public string ReadPolicy()
{
    Guard();
    workflow.Store.Record(state, "read_policy", "Read only the candidate policy.");
    return File.ReadAllText(workflow.Store.PolicyPath);
}
```

<!-- excerpt: ../../agent/Demo.Agent/WorkspaceTools.cs -->
```csharp
var allowed = (original == Workflow.ThresholdBefore && replacement == Workflow.ThresholdAfter) ||
              (original == Workflow.RoundingBefore && replacement == Workflow.RoundingAfter);
if (!allowed)
    throw new InvalidOperationException("Denied: only the reviewed threshold and explicit-rounding patches are allowed. No arbitrary code or paths.");
var path = workflow.Store.PolicyPath;
var source = File.ReadAllText(path);
File.WriteAllText(path, source.Replace(original, replacement, StringComparison.Ordinal));
```

- **`Guard()` runs before every tool call:** enforces max 10 tool calls and re-verifies the SHA-256 hash of `spec.md` and `Claims.Acceptance`.

[`ReadPolicy` (`WorkspaceTools.cs:29`)](source.html?file=agent/Demo.Agent/WorkspaceTools.cs#line-29) · [`ReplaceExpression` (`WorkspaceTools.cs:41`)](source.html?file=agent/Demo.Agent/WorkspaceTools.cs#line-41) · [`Verify` (`WorkspaceTools.cs:64`)](source.html?file=agent/Demo.Agent/WorkspaceTools.cs#line-64)

Note:
Walk through the actual C# implementation in `WorkspaceTools.cs` when the Copilot SDK model calls a tool:
1. Every tool call first runs `Guard()`, which checks the tool-call budget (max 10 calls) and calls `workflow.ValidateBaseline(state)` to ensure no spec or test file hash has changed.
2. `ReadPolicy()` takes no path argument — it only reads `workflow.Store.PolicyPath` (`workspace/app/Claims.Service/CompensationPolicy.cs`).
3. `ReplaceExpression(original, replacement)` checks that the edit matches the reviewed threshold or rounding change, verifies the expression appears once in `CompensationPolicy.cs`, writes the updated C# file, and records the audit event.
4. `Verify()` compiles the workspace and executes the external `Claims.Acceptance` test runner.

---

<!-- .slide: id="results" -->

Step 4 / Execute & Verify (`execute my-demo` → `Claims.Acceptance`)

## Tool dispatch trace, code diff, and 12 acceptance checks.

**1. Resulting C# fix in `CompensationPolicy.cs` (`>= 60` + `Rounding` switch):**

<!-- excerpt: captured/after-policy.cs.txt -->
```csharp
var percent = delayMinutes >= 120 ? 50 : delayMinutes >= 60 ? 25 : 0;
var numerator = checked(fareRappen * percent);
return rounding switch
{
    Rounding.Down => numerator / 100,
```

**2. Captured SDK tool dispatch & test runner output (`sdk-runtime-test.log`):**

<!-- excerpt: captured/sdk-runtime-test.log -->
```text
  FAIL FR-002: Boundary at 60 minutes - Expected 1500 rappen; actual 0.
  FAIL FR-004: Explicit fractional-rappen rounding - Expected 250 rappen; actual 0.
[Executing] baseline: Real acceptance failures captured before repair.
[SDK] tool: read_policy
[SDK] tool: replace_expression
[SDK] tool: replace_expression
[SDK] tool: verify
  PASS FR-002: Boundary at 60 minutes
  PASS FR-004: Explicit fractional-rappen rounding
[AwaitPublicationApproval] verify: PASS: candidate verified; export still requires separate approval.
```

[Before code](source.html?file=demo/presentation/captured/before-policy.cs.txt#line-16) · [After code](source.html?file=demo/presentation/captured/after-policy.cs.txt#line-16) · [SDK runtime log](captured/sdk-runtime-test.log) · [Rehearsal log](captured/04-execute.log) · [Exported `review.md`](source.html?file=demo/presentation/captured/review.md)

Note:
Show the real captured output from running `execute`:
1. First, the baseline acceptance run executes and fails on `FR-002` (60 minutes returned 0 instead of 1500 rappen) and `FR-004` (250.75 rounded to 251 instead of 250).
2. Next, watch the `[SDK] tool:` events emitted by `CopilotClient`: `read_policy` → `replace_expression` (threshold `>= 60`) → `replace_expression` (rounding switch) → `verify`.
3. `verify` compiles the patched `CompensationPolicy.cs` and runs all 12 checks in `Claims.Acceptance`: both `FR-002` and `FR-004` turn green while all regression checks stay green.
4. Finally, running `export my-demo --by reviewer` writes `review.md` and `evidence.json`.

---

<!-- .slide: id="run" -->

Step 5 / Package & Host (`dotnet publish`)

## Package the .NET agent and bundled Copilot CLI runtime.

```bash
dotnet publish agent/Demo.Agent -c Release -o .demo/publish
dotnet .demo/publish/Demo.Agent.dll --help
```

<!-- excerpt: captured/published-help.log -->
```text
SDD / Copilot SDK demo (.NET only)
```

| What is inside `.demo/publish/` | What comes from outside at runtime |
|---|---|
| `Demo.Agent.dll` (controller + tool guardrails) | Target repo & Spec Kit artifacts (`specs/`, `app/`) |
| `GitHub.Copilot.SDK.dll` + bundled `copilot` CLI binary | Model endpoint (`COPILOT_GITHUB_TOKEN` or BYOK `DEMO_MODEL_*` for AI Gateway / Foundry) |

*Note: Azure DevOps PR creation and Foundry container hosting are the natural deployment targets, kept out of this local repo.*

[Agent `.csproj`](source.html?file=agent/Demo.Agent/Demo.Agent.csproj) · [Publish log](captured/publish.log) · [Packaged rehearsal log](captured/packaged-run.log) · [Model / BYOK setup](source.html?file=README.md)

Note:
Summarize how the agent is packaged and where it fits in the client's platform:
1. `dotnet publish` produces a standard .NET output directory containing our controller DLL, `GitHub.Copilot.SDK.dll`, and the platform-specific Copilot CLI binary under `runtimes/`.
2. We verified both `--help` and a full end-to-end rehearsal using the published `Demo.Agent.dll`.
3. To run in production (for example hosted on Azure AI Foundry with an AI Gateway and Azure DevOps pipelines), you supply the model/gateway credentials via environment variables (`DEMO_MODEL_ENDPOINT`, `DEMO_PROVIDER`, etc.) and wire the `export` step to open an ADO pull request.

---

<!-- .slide: id="outlook" -->

Outlook & Discussion / End-to-End Agentic SDLC

## What if Step 1 (`specify` & `clarify`) also runs as a Copilot SDK agent on ADO?

Because Spec Kit (`speckit-*`) is just standard `SKILL.md` files, the same `SkillDirectories` SDK feature can automate intake right on an Azure DevOps work item:

| Stage in Azure DevOps | Who runs | Skills & Tools loaded in `CopilotClient` |
|---|---|---|
| **1. Ticket Intake Agent** (ADO Webhook) | `GitHub.Copilot.SDK` | Loads `speckit-specify` + `speckit-clarify` → drafts `spec.md` and **posts the `250.75` rappen question as an ADO comment** |
| **2. Product Owner Gate** (Azure Boards) | Human PO & Engineer | PO replies `"Round down"` on the ADO work item and approves the spec/test contract |
| **3. Implementation Agent** (Pipeline / Foundry) | `GitHub.Copilot.SDK` | Loads `demo-policy-change` + C# tools (`read_policy`, `replace_expression`, `verify`) → opens verified ADO PR |

**Question for you:** Where in your ADO workflow would an automated **Spec & Clarify intake agent** save the most back-and-forth today?

[Specify skill](source.html?file=.github/skills/speckit-specify/SKILL.md) · [Clarify skill](source.html?file=.github/skills/speckit-clarify/SKILL.md) · [Execution skill](source.html?file=.github/skills/demo-policy-change/SKILL.md) · [SDK session setup](source.html?file=agent/Demo.Agent/SdkWorker.cs#line-84)

Note:
Use this slide to open the architectural discussion with the client:
1. In our local demo, we ran Step 1 (`speckit-specify` and `speckit-clarify`) interactively in the IDE and only ran Step 3 (`demo-policy-change`) inside the .NET Copilot SDK worker.
2. Ask the audience: "Why stop there? Since Spec Kit skills use the exact same `SKILL.md` format that `GitHub.Copilot.SDK` loads via `SkillDirectories`, we can run a Spec & Clarify agent directly on new ADO work items."
3. Walk through the two-agent handoff:
   - Agent 1 (Intake) watches new ADO tickets, generates `spec.md`, spots missing edge cases like the 250.75 rappen rounding rule, and posts the clarification question straight back into the ADO ticket discussion before a developer ever picks it up.
   - Once the Product Owner answers in ADO, Agent 2 (Implementation) runs with locked test hashes and opens a verified ADO Pull Request.

---

<!-- .slide: id="benefits" -->

Takeaways / Why this architecture works

## One shared contract across the IDE, the repo, and headless agents.

| Benefit | How it works in practice |
|---|---|
| **1. IDE & headless agents stay aligned** | Developers in any IDE/CLI assistant and headless `GitHub.Copilot.SDK` agents read the exact same `AGENTS.md`, `constitution.md`, and `SKILL.md` files |
| **2. Innersourcing through the repo** | Skills (`.github/skills/`), domain rules, and specs are versioned in git — teams share, review, and reuse engineering practices across repos via normal PRs |
| **3. ADO + Git work together** | Business intake & approvals happen in **Azure DevOps work items**, while `spec.md`, `plan.md`, and `tasks.md` live next to the code and travel with the branch/PR |
| **4. Deterministic guardrails around AI** | C# host code owns workspace setup, SHA-256 test/spec locks, and `AIFunctionFactory` tool boundaries — the model cannot weaken tests to make itself green |
| **5. Model & platform flexibility** | `CopilotClient` handles the agent/tool loop while `ProviderConfig` routes inference to your chosen model, AI Gateway, or Azure AI Foundry endpoint |

[Repository guidance (`AGENTS.md`)](source.html?file=AGENTS.md) · [Constitution](source.html?file=.specify/memory/constitution.md) · [BYOK / Provider setup (`SdkWorker.cs:8`)](source.html?file=agent/Demo.Agent/SdkWorker.cs#line-8) · [Exported review bundle](source.html?file=demo/presentation/captured/review.md)

Note:
Close the presentation on these five concrete takeaways:
1. Developer IDE workflow and background agents are aligned: there is no "prompt drift" between how an engineer works interactively in their IDE/CLI and how the automated Copilot SDK agent runs in a pipeline, because both read `AGENTS.md`, `.specify/memory/constitution.md`, and `.github/skills/*/SKILL.md`.
2. Innersourcing via git: teams package domain knowledge, coding standards, and workflow skills as markdown files in the repo, making them easy to share and evolve across teams.
3. Seamless bridge between Azure DevOps and Git: Product Owners stay in Azure Boards for tickets and policy decisions, while engineers and agents get version-controlled `spec.md`, `plan.md`, and `tasks.md` right alongside the code and PR.
4. Hard C# guardrails: AI handles the reasoning and tool calls, while deterministic C# enforces approvals, test hashes, and file scope.
5. Platform choice: you get the Copilot SDK runtime and tool loop while routing inference through your enterprise AI Gateway / Azure AI Foundry models.

