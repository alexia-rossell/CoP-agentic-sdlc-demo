# Demo: from an approved spec to a verified ADO change

**15 minutes.** A fictional Swiss mobility company changes its compensation
policy. Spec Kit structures the change; a .NET Copilot SDK application works on a
small .NET claims target. ADO remains the intended delivery platform.

**Ready locally:** Spec Kit artifacts, claims fixture, SDK/rehearsal workflow,
real acceptance checks, checkpoints and evidence export. See [steps](demo/STEPS.md).
**Not yet connected:** live ADO publication, Foundry hosting and cloud traces.

## Walkthrough

| Time | Do / show | Question to raise |
|---|---|---|
| 0-1 | Synthetic ADO-style request: 25% from 60 minutes, 50% from 120. | What may the agent decide without asking? |
| 1-3 | Spec Kit artifacts. Clarify fractional-rappen rounding; show the spec change. | Who owns this business rule: product owner or model? |
| 3-4 | Updated plan/tasks, consistency check, reviewed tests and explicit approval. | What happens if the approved contract changes? |
| 4-8 | SDK worker, real tool events, failing 60-minute test, narrow repair, passing tests. | Can the worker weaken a test to make itself green? |
| 8-10 | Convergence review and requirement -> test -> code evidence. | What does a green test suite prove, and not prove? |
| 10-12 | Restart at a safe checkpoint; resume with publication still awaiting approval. | What must survive beyond the conversation? |
| 12-14 | Approve local evidence export. Explain the ADO handoff and Foundry hosting as next integrations; show them live only once connected. | Can a reviewer trace the change to its approved requirement? |
| 14-15 | Recap: portable artifacts, chosen tools/model, application-enforced controls. | Which repeatable change would you try first? |

## Presenter essentials

- **SDD:** clarify -> specify -> plan -> tasks -> implement -> verify/converge.
  Use Spec Kit analysis before implementation; do not present generated prose as approval.
- **Skills / AGENTS.md:** reusable process and repository guidance, not permission enforcement.
- **Graph:** application-owned stages and bounded repair; one SDK worker is sufficient.
- **Memory:** SDK session continuity plus durable application checkpoints and approvals.
- **Foundry:** a short platform-continuity note, not a hosting tutorial. Host the
  service there and route inference through the configured AI gateway; demonstrate
  these only after verification.
- **Positioning:** BYOK can avoid Copilot authentication for inference; the SDK
  still uses the Copilot CLI runtime. ADO does not require GHE.

## Rehearse before presenting

1. Select live SDK or labelled rehearsal mode; verify the chosen model/gateway separately.
2. Prepare the boundary defect and reviewed acceptance tests; use synthetic data only.
3. Confirm protected specs/tests cannot be changed by the implementation worker.
4. Measure the live run; keep a labelled recording as fallback. Never invent results.

## Start here

- [Demo steps and copy-ready prompts](demo/STEPS.md)
- [Draft specification](specs/001-delay-compensation/spec.md)
- [Implementation plan](specs/001-delay-compensation/plan.md)
- [Tasks](specs/001-delay-compensation/tasks.md)
- [Constitution](.specify/memory/constitution.md)

References: [Spec Kit](https://github.com/github/spec-kit),
[SDK / BYOK](https://github.com/github/copilot-sdk/blob/main/docs/auth/byok.md),
[Foundry hosted agents](https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/hosted-agents).
