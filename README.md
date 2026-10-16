# Spec-Driven Development + GitHub Copilot SDK (.NET Demo)

This demo shows how an engineering team combines **Spec Kit** (interactive Spec-Driven Development in the IDE) with a **.NET 10 agent built on `GitHub.Copilot.SDK`** to deliver a verified policy change:

1. **Developer in IDE (Spec Kit):** Takes a rough Product Owner / Azure DevOps ticket (*"25% delay refund at 60–119 min, 50% at 120+ min"*), runs `/speckit-specify` and `/speckit-clarify` to uncover a missing rounding rule (`CHF 10.03 × 25% = 250.75 rappen`), and generates `spec.md`, `plan.md`, and `tasks.md`.
2. **Custom .NET Controller (`agent/Demo.Agent`):** Copies the approved spec and target app (`app/`) into an isolated run workspace and locks SHA-256 hashes over the spec and acceptance tests (`Claims.Acceptance`).
3. **GitHub Copilot SDK Worker (`SdkWorker.cs`):** Starts a `CopilotClient` session with three scoped C# tools (`read_policy`, `replace_expression`, `verify`) and an execution skill (`demo-policy-change`) to patch `CompensationPolicy.cs` and pass all 12 acceptance checks.
4. **Outlook (Full Agentic SDLC):** Raises the closing question—since Spec Kit skills are standard `SKILL.md` files, we can also embed `/speckit-specify` and `/speckit-clarify` inside a Copilot SDK **Intake Agent** right on the ADO ticket.

## Prerequisites & One-Time Setup

| Component | Required tools | Install / build command |
|---|---|---|
| **1. Presentation** (`demo/presentation/`) | Node.js / `npm` + `python3` | `npm ci --prefix demo/presentation` |
| **2. .NET App & Agent** (`app/`, `agent/`) | **.NET 10 SDK** (`10.0.402+`, pinned in `global.json`) | `dotnet restore Demo.slnx --locked-mode && dotnet build Demo.slnx --no-restore` |
| **3. Spec Kit** (`.specify/`, `.github/skills/`) | **Specify CLI** (`specify-cli`) + `git` *(skills are already checked into `.github/skills/`)* | `uv tool install specify-cli --from git+https://github.com/github/spec-kit.git` |

*(On this machine, `.NET 10` is in `~/.dotnet` and `specify` is in `~/.local/bin`, so add both to `PATH`: `export PATH="$HOME/.dotnet:$HOME/.local/bin:$PATH"`.)*

---

## 1. Click through the Reveal.js presentation

The slide deck walks through every step with direct links to the source files, skills, and captured test outputs.

```bash
npm ci --prefix demo/presentation
npm start --prefix demo/presentation
```

Open **<http://127.0.0.1:4180/demo/presentation/>** in your browser:
- **Arrow keys / Space:** Next / previous slide
- **`S`:** Open presenter speaker notes
- **`Esc`:** Slide overview
- **Edit slides:** [demo/presentation/slides.md](demo/presentation/slides.md)

---

## 2. Live Demo Walkthrough (15 min)

### Part A — Developer perspective: Spec Kit in the IDE
Before touching `dotnet run`, show how the developer uses Spec Kit in their coding assistant (Copilot Chat or Claude CLI):

1. **Constitution & Guardrails:** Open [.specify/memory/constitution.md](.specify/memory/constitution.md) (integer rappen, no silent rounding guesses).
2. **From ADO ticket to `spec.md` (`/speckit-specify`):**
   Show how a rough PO request becomes [specs/001-delay-compensation/spec.md](specs/001-delay-compensation/spec.md) and why the AI flags `FR-004` with `[NEEDS CLARIFICATION]`:
   ```text
   /speckit-specify Pay 25% compensation for delays of 60-119 minutes and 50% from 120 minutes.
   ```
3. **Interactive Clarification (`/speckit-clarify`):**
   Run `/speckit-clarify` in the IDE chat to show how it scans `spec.md` and asks the Product Owner to choose `down`, `up`, or `nearest` for `250.75` rappen:
   ```text
   /speckit-clarify Focus on FR-004 fractional-rappen rounding. Ask the product owner for the decision.
   ```
   *(Keep `git checkout -- specs/001-delay-compensation/spec.md` afterwards if you want the repo draft to stay unresolved for the next run.)*
4. **Plan & Tasks (`/speckit-plan`, `/speckit-tasks`):**
   Open [plan.md](specs/001-delay-compensation/plan.md) and [tasks.md](specs/001-delay-compensation/tasks.md) to show how the clarified spec maps to `app/Claims.Service/CompensationPolicy.cs` and `app/Claims.Acceptance/Program.cs`.

### Part B — Automated execution: .NET Controller + GitHub Copilot SDK
Once the developer and PO agree on the spec (`down`), hand off to the `.NET 10` controller (`agent/Demo.Agent`):

```bash
export PATH="$HOME/.dotnet:$HOME/.local/bin:$PATH"

# 1. Create an isolated sandbox (.demo/runs/walkthrough/) with the 2 seeded bugs
dotnet run --project agent/Demo.Agent -- init walkthrough

# 2. Stamp the PO's rounding answer ("down") into this run's spec snapshot
dotnet run --project agent/Demo.Agent -- clarify walkthrough --rounding down

# 3. Lock SHA-256 hashes of the spec and Claims.Acceptance test suite
dotnet run --project agent/Demo.Agent -- approve walkthrough --by presenter

# 4. Run the worker (use --mode sdk with model credentials, or --mode rehearsal offline)
dotnet run --project agent/Demo.Agent -- execute walkthrough --mode rehearsal

# 5. Export the verified diff and requirement-to-test matrix
dotnet run --project agent/Demo.Agent -- export walkthrough --by reviewer
```

Inspect `.demo/runs/walkthrough/export/review.md` to see the before/after code diff (`delayMinutes >= 60` and `Rounding.Down`) and the 12 passing acceptance checks.

### Part C — Package & Closing Discussion
Show that the agent packages cleanly as a standard `.NET` artifact with the bundled Copilot CLI runtime:

```bash
dotnet publish agent/Demo.Agent -c Release -o .demo/publish
dotnet .demo/publish/Demo.Agent.dll --help
```

Close on **Slide 7 (`#outlook`)**: *Since `/speckit-specify` and `/speckit-clarify` are just `SKILL.md` files, we can also load them into a Copilot SDK agent triggered by new ADO work items—so missing edge cases like `250.75` rappen are asked on the ticket before a developer even opens the IDE.*

---

## 3. Configuring Live SDK / BYOK Inference (Optional)

To run `execute <id> --mode sdk` against a live model or AI Gateway / Foundry endpoint:

```bash
export DEMO_MODEL="your-model-or-deployment"
# Option A: GitHub Copilot token
export COPILOT_GITHUB_TOKEN="ghp_..."

# Option B: BYOK / AI Gateway / Foundry endpoint
export DEMO_MODEL_ENDPOINT="https://your-gateway.example/openai/v1"
export DEMO_PROVIDER="openai"      # openai | azure | anthropic
export DEMO_WIRE_API="completions" # completions | responses
export DEMO_MODEL_API_KEY="..."
```

Run the full unit + local SDK runtime integration suite anytime with:

```bash
dotnet test tests/Demo.Tests/Demo.Tests.csproj
```
