# Presenter walkthrough: requirement → question → verified local candidate

Use the exact commands in [README](../README.md#live-commands); provision model
access and any hosted deployment separately. No initial story has been created.
All remote writes below require an explicit presenter action. The configured
organization/project are `swissreghcpsdkdemo` / `CoP-agentic-sdlc-demo`.

**When presenting the deployed agent**, replace each local
`dotnet run --no-build --project agent/Demo.Agent --` prefix with
`python3 deploy/client.py`. The thin client invokes the controller in Foundry;
it does not run the workflow locally. See the
[remote walkthrough](../deploy/README.md#what-the-interaction-looks-like) for
deployment, authentication and the deliberate single-session limitation.

1. **Setup, separately approved:** `create-story live1 --by presenter
   --approve-create`. Note its new ID/URL. It creates an Agile User Story from
   `demo/ticket.json`; FR-004 remains unresolved. No prior Bug is reused.
2. **Intake:** `init live1`. Show the User Story and protected acceptance scope.
   The single defect is seeded only in this run's private workspace.
3. **Agent asks:** `ask live1 --by presenter`. Show the rounding question in ADO
   History. This flag-bearing command is the permission to post, not PO approval.
4. **Human edits ADO:** replace the FR-004 AC clause with the actual answer:
   `FR-004: Rounding: down`, `FR-004: Rounding: up`, or
   `FR-004: Rounding: nearest`. Choose only after the actual PO decision.
   Preserve every other requirement; do not answer only in a comment.
5. **Next invocation verifies:** `check live1`. Show the read-back revision and
   `decision.json` evidence. Ambiguous/unresolved answers stay blocked.
   `check` grants no approval; local `clarify` cannot override live ADO.
6. **Scope approval:** `approve live1 --by presenter`. Review ticket, answer and
   test oracle. Content changes invalidate this approval.
7. **Real baseline:** `test live1`. Show `baseline-results.json` and actual red
   FR-002 boundary checks. A compiler failure is not a successful baseline.
8. **Linked Bug approval:** `open-bug live1 --by presenter
   --approve-create-bug`. Show the new Bug, actual failures and parent Story link.
   Story AC remains authoritative; the Bug is a synthetic workspace defect.
9. **Bounded model repair:** `execute live1 --mode sdk`. The SDK receives source,
   ticket and failures, not a prescribed fix. Show real verification and all
   12 checks. SDK tools cannot change rounding, ledger, validation or tests.
10. **Review/export:** `export live1 --by reviewer`. Inspect the before/candidate
    diff, logs, JSON and `export/review.md`. This does not commit, push or deploy.
11. **Publication permission:** `publish live1 --by reviewer`. Show the Story
    History with mode, rounding, candidate hash, linked Bug and test summary.
12. **Optional, separate resolution:** `resolve-bug live1 --by reviewer
    --ack-local-candidate`. This revision-guarded transition marks the synthetic
    Bug `Resolved` only for the tested local candidate. It explicitly says not
    committed/integrated/deployed. Do not use it where production integration
    is required first. User Story state is not auto-closed.

For deterministic offline rehearsal, use a fresh `init-offline` run, obtain the
actual per-run answer via `clarify`, then `approve`, `execute --mode rehearsal`,
and `export`. Skip all remote commands. It uses real compilation/tests but no
real model and must be presented as **OFFLINE REHEARSAL**.

## Return to a clean demo

`reset live1 --by presenter --approve-reset` deletes only persisted,
ownership-verified demo Bug IDs and Story ID, then recreates the unresolved
requirement with a **new ID**. Run `init live1` again. Existing unrelated Bug 1
is untouched. Same-ID history erasure is not possible; soft deletion normally
retains old items in ADO's recycle bin. No permanent delete is implemented.
See README for partial-failure/retry rules and journal persistence requirements.

Hosted invocation `args` are the same command tokens. The out-of-band presenter
token is additionally required; never give it to the model. Use durable,
single-writer storage for `.demo`, or perform managed setup/reset locally:
container replacement/routing can otherwise lose the deletion allow-list.
