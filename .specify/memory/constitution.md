# Mobility change demo constitution

**Status:** proposed project principles; human ratification pending.

## Core principles

### I. Specify intent before implementation

Version the behaviour, non-goals and acceptance criteria. Resolve business
ambiguities with a human, then review the plan and tasks before coding.
An approved contract change requires renewed review.

### II. Test the contract, not the implementation

Write and review acceptance tests before changing policy code. Observe the
prepared boundary failure, then fix it without weakening the expected results.
Retain regression coverage and map requirements to evidence.

### III. Keep money exact

Use integer rappen and explicitly defined rounding. Validate inputs and supported
ranges; do not use floating-point money or invent a business default.

### IV. Bound the agent's authority

Use isolated workspaces, scoped tools, execution budgets and explicit failures.
Implementation workers must not alter approved contracts or test oracles.
The application must enforce approval before remote writes; instructions alone
are not enforcement.

### V. Prefer portable, reviewable evidence

Keep specifications and tests tool-neutral. Use ADO for the proposed delivery
integration. Persist workflow decisions separately from conversational context.
Report real commands/results and distinguish prepared material from live output.

## Scope and workflow

Use fictional requirements and synthetic data only. No real payouts, production
credentials, autonomous merge, or production deployment are in scope.
Foundry hosting is a small integration concern, not the feature's business logic.

Follow specify/clarify -> plan/checklist -> tasks/analyze -> approve ->
test/implement -> verify/converge -> approve publication.
Convergence findings do not authorise new scope or business-policy decisions.

## Governance

Review changes to these principles explicitly. Record actual approval references
when a workflow is implemented; do not treat these prepared documents as approval.
Major versions change principles incompatibly; minor versions add principles;
patch versions clarify wording.

**Version:** 0.1.0 | **Ratified:** pending | **Last amended:** 2026-10-16
