# Presenter steps

See [quick start](../README.md) and [15-minute agenda](../DEMO_AGENDA.md).
Everything below is local unless explicitly configured for real SDK inference.

## 1. Show the engineering contract

Open the [constitution](../.specify/memory/constitution.md),
[spec](../specs/001-delay-compensation/spec.md), and
[plan/tasks](../specs/001-delay-compensation/plan.md).

Explain: the unresolved rounding question is intentional demo input, not an
unfinished implementation of the demo. The target and host are both .NET.

## 2. Create a fresh run

```bash
dotnet run --project agent/Demo.Agent -- init walkthrough
```

Show `.demo/runs/walkthrough/workspace/app/Claims.Service/CompensationPolicy.cs`:
the prepared `> 60` defect and fixed-nearest rounding. Repository source remains
correct. Use a new run ID for another presentation; init never silently resets.

## 3. Clarify

Ask: "How should fractional rappen be rounded?"
For the intended scenario, answer "down".

```bash
dotnet run --project agent/Demo.Agent -- clarify walkthrough --rounding down
```

Show the resolved per-run `artifacts/spec.md` and `decision.json`.
The original repository draft remains unresolved for the next walkthrough.
`up` and `nearest` (half-up) are also explicit supported decisions.

Spec Kit clarification can be demonstrated interactively in a disposable copy:

```text
/speckit-clarify Focus on FR-004, fractional-rappen rounding. Ask the product owner
and update the acceptance example. Do not implement code.
```

This edits that copy's spec; it is separate from the application's run-specific
decision command. Do not claim the command invokes Spec Kit automatically.

## 4. Review and approve

Show reviewed acceptance cases in `app/Claims.Acceptance/Program.cs`.
The plan and task list are prepared; show `/speckit-analyze` separately if desired.

```bash
dotnet run --project agent/Demo.Agent -- approve walkthrough --by presenter
```

This records a local presenter label, timestamp and artifact/test hash.
It is not enterprise-authenticated approval.

## 5. Execute

For real model/tool selection, configure inference as described in the README:

```bash
dotnet run --project agent/Demo.Agent -- execute walkthrough --mode sdk
```

For reliable offline rehearsal, use `--mode rehearsal` instead. It is explicitly
labelled: scripted tool selection, real compilation and acceptance results.
SDK failure never silently switches modes.

Show the real baseline failure, SDK tool events, two bounded patches and real
candidate results. The worker cannot edit tests or arbitrary code.
This is intentionally narrower than a general-purpose coding agent.

## 6. Inspect and resume

```bash
dotnet run --project agent/Demo.Agent -- status walkthrough
```

Each command is a new process. The stage is `AwaitPublicationApproval`; tests,
candidate hash and baseline approval survive. Nothing has been published.
This demonstrates application state recovery, not resumed SDK conversation.

Spec Kit `/speckit-converge` is a separate assessment. The application does not
pretend its test runner is that assessment.

## 7. Approve local export

```bash
dotnet run --project agent/Demo.Agent -- export walkthrough --by reviewer
```

Open `.demo/runs/walkthrough/export/review.md`: requirement/test matrix,
before/after source, actual reports and local approval evidence.
Repeat export safely; there is no ADO write or deployment.

## 8. Close

The artifacts and workflow are independent of GitHub repository hosting.
BYOK routes model inference to a configured endpoint while retaining the Copilot
runtime. ADO publication and Foundry hosting are not implemented here yet;
mention them briefly as platform integrations, not demonstrated capabilities.
