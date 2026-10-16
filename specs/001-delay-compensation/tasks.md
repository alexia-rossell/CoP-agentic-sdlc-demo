# Demo tasks: delay compensation

**Input:** [Spec as demo input](spec.md), [demo implementation plan](plan.md)
**Scope update:** .NET only; the repository's fictional rounding question stays open.

## Implement the demo

- [x] T001 Pin .NET SDK/packages and create `Demo.slnx`, target, acceptance, host and test projects.
- [x] T002 Implement exact-money policy, validation and single-process duplicate/conflict handling in `app/Claims.Service/`.
- [x] T003 After T002, create the independent boundary/rounding/regression suite in `app/Claims.Acceptance/`.
- [x] T004 After T003, prepare defects only in per-run workspace copies in `agent/Demo.Agent/Workflow.cs`.
- [x] T005 After T004, implement explicit per-run clarification/approval, baseline hashes, restricted patches and budgets in `agent/Demo.Agent/`.
- [x] T006 After T005, wire real SDK tool execution, BYOK configuration and explicit skill loading in `agent/Demo.Agent/SdkWorker.cs`.
- [x] T007 After T005, implement durable local checkpoints, separate export approval and evidence export in `agent/Demo.Agent/`.
- [x] T008 After T006 and T007, add target/workflow tests and a real SDK-runtime integration test using a local scripted provider in `tests/Demo.Tests/`.
- [x] T009 After T008, complete final regression/CLI rehearsal and update `README.md`, `demo/STEPS.md`, `DEMO_AGENDA.md`.

## Present the fictional change (not pre-approved)

- [ ] T010 [US1] Ask the presenter for rounding during the walkthrough; record only in that run's artifact snapshot.
- [ ] T011 After T010, review/approve that run's contract and acceptance oracle.
- [ ] T012 After T011, execute the live SDK mode against the selected real model and inspect actual evidence.
- [ ] T013 After T012, run a separate Spec Kit convergence assessment if showing that step; do not substitute a test result for it.

## Optional external integrations (not implemented or claimed)

- [ ] T014 Add credential-scoped ADO publication with candidate approval and remote retry reconciliation.
- [ ] T015 Add/verify Foundry hosted-agent deployment, actual gateway routing and a correlated cloud trace.

The unchecked requirements checklist belongs to the presentation contract.
The user authorised implementing the demo without resolving that contract.
