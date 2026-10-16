# Presenter steps

See [README](../README.md) and the [Reveal.js slide deck](presentation/slides.md) (`npm start --prefix demo/presentation` → <http://127.0.0.1:4180/demo/presentation/>).

The demo has **three parts**:
1. **Developer in the IDE with Spec Kit** (how a developer turns a rough PO/ADO ticket into a clarified spec and plan)
2. **Automated execution with `.NET` Controller + `GitHub.Copilot.SDK`** (how the approved spec is locked, executed, and verified)
3. **Packaging & Closing Outlook** (packaging the `.NET` agent and asking why not also run `specify` + `clarify` inside a Copilot SDK Intake Agent on ADO)

---

## Part 1 — Developer perspective: Spec Kit in the IDE (Slides 1–2)

Do **not** start with `dotnet run`. Start by showing how the developer works with Spec Kit in their IDE coding assistant (Copilot Chat or Claude CLI):

1. **Show the rough PO / ADO ticket & Constitution:**
   - Business ask: *"Pay 25% compensation for delays of 60–119 minutes and 50% from 120 minutes."*
   - Open [.specify/memory/constitution.md](../.specify/memory/constitution.md): shows the project rules (integer rappen, no silent rounding assumptions).

2. **Show `/speckit-specify` (Raw ticket → `spec.md`):**
   - Open [.github/skills/speckit-specify/SKILL.md](../.github/skills/speckit-specify/SKILL.md) and [specs/001-delay-compensation/spec.md](../specs/001-delay-compensation/spec.md).
   - Explain or run in IDE chat:
     ```text
     /speckit-specify Pay 25% compensation for delays of 60-119 minutes and 50% from 120 minutes.
     ```
   - Point out line 45 (`FR-004`): because `CHF 10.03 × 25% = 250.75 rappen` has no single obvious default, `/speckit-specify` flags `[NEEDS CLARIFICATION]` instead of guessing.

3. **Run `/speckit-clarify` live in the IDE chat:**
   - Run in your IDE assistant:
     ```text
     /speckit-clarify Focus on FR-004, fractional-rappen rounding. Ask the product owner and do not implement code yet.
     ```
   - Show how the skill scans `spec.md` and presents the multiple-choice question (`down`, `up`, `nearest`) for the Product Owner.
   - *(If you let the IDE edit `spec.md` live, run `git checkout -- specs/001-delay-compensation/spec.md` afterwards so the repo stays ready for the CLI run.)*

4. **Show `/speckit-plan` and `/speckit-tasks`:**
   - Open [plan.md](../specs/001-delay-compensation/plan.md) and [tasks.md](../specs/001-delay-compensation/tasks.md).
   - Show how the clarified spec is mapped to `app/Claims.Service/CompensationPolicy.cs` and the 12 acceptance checks in `app/Claims.Acceptance/Program.cs`.

---

## Part 2 — Automated execution: `.NET` Controller + Copilot SDK (Slides 3–5)

Now transition from the interactive IDE to the automated `.NET` host (`agent/Demo.Agent`):

### 1. Prepare & lock the run (`init`, `clarify`, `approve` — deterministic C#, no AI)

```bash
export PATH="$HOME/.dotnet:$HOME/.local/bin:$PATH"

dotnet run --project agent/Demo.Agent -- init walkthrough
dotnet run --project agent/Demo.Agent -- clarify walkthrough --rounding down
dotnet run --project agent/Demo.Agent -- approve walkthrough --by presenter
```

- Show `.demo/runs/walkthrough/workspace/app/Claims.Service/CompensationPolicy.cs` (has the `> 60` and rounding bugs).
- Show `.demo/runs/walkthrough/artifacts/spec.md` (`FR-004` is now stamped with `down`) and `.demo/runs/walkthrough/state.json` (SHA-256 hash locking the spec and `Claims.Acceptance` tests).

### 2. Run the Copilot SDK worker (`execute`)

Open [agent/Demo.Agent/SdkWorker.cs](../agent/Demo.Agent/SdkWorker.cs) and [agent/Demo.Agent/WorkspaceTools.cs](../agent/Demo.Agent/WorkspaceTools.cs) to show:
- `CopilotClient` loads `.github/skills/demo-policy-change/SKILL.md` (the execution skill, **not** `speckit-*`).
- `AIFunctionFactory` exposes only 3 C# tools: `read_policy`, `replace_expression`, `verify`.

Run the worker (use `--mode sdk` with configured model credentials, or `--mode rehearsal` offline):

```bash
dotnet run --project agent/Demo.Agent -- execute walkthrough --mode rehearsal
```

Point out the output:
1. Baseline `Claims.Acceptance` run fails 2 of 12 checks (`FR-002` 60-min boundary and `FR-004` rounding).
2. Worker calls `read_policy` → `replace_expression` (twice) → `verify`.
3. All 12 acceptance checks pass, and the controller verifies the test/spec hashes were not touched.

### 3. Export review evidence (`status`, `export`)

```bash
dotnet run --project agent/Demo.Agent -- status walkthrough
dotnet run --project agent/Demo.Agent -- export walkthrough --by reviewer
```

Open `.demo/runs/walkthrough/export/review.md` to show the traceable diff and requirement-to-test matrix ready for an Azure DevOps Pull Request.

---

## Part 3 — Package & Closing Question (Slides 6–7)

### 1. Package the agent (`dotnet publish`)

```bash
dotnet publish agent/Demo.Agent -c Release -o .demo/publish
dotnet .demo/publish/Demo.Agent.dll --help
```

Show that `.demo/publish/` bundles `Demo.Agent.dll`, `GitHub.Copilot.SDK.dll`, and the platform-specific `copilot` CLI binary, ready to run with environment variables pointing to an AI Gateway / Azure AI Foundry model endpoint.

### 2. Raise the closing question (Slide 7 — `#outlook`)

Ask the audience:
> *"In Part 1, a developer ran `/speckit-specify` and `/speckit-clarify` in the IDE. In Part 2, `GitHub.Copilot.SDK` ran the `demo-policy-change` skill headlessly. Since Spec Kit is also just `SKILL.md` files, what if we put `/speckit-specify` and `/speckit-clarify` into a Copilot SDK **Intake Agent** triggered by new Azure DevOps work items?"*

Walk through the 3-stage table on Slide 7:
1. **ADO Webhook → Intake Agent (`CopilotClient` + `speckit-specify` / `speckit-clarify`):** Drafts `spec.md` and automatically posts the `250.75` rappen rounding question as an ADO comment.
2. **Product Owner in Azure Boards:** Replies `"Round down"` and approves the contract.
3. **Pipeline / Foundry → Implementation Agent (`CopilotClient` + `demo-policy-change`):** Applies the fix against locked tests and opens a verified ADO PR.
