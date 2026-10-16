# Local demo evidence: fallback-20261016

Mode: rehearsal. Rounding: down.
Baseline approval: F3A6C075C38D725D4942EC17FFA0629D2550E81E7B49F440B9F6A81FC3E3A20E.
Candidate: 0395E04B9A6875E2CDE6C36021EF70C2FB77CF5D716043CB3D418666F43D0B5A.

Compare `before-policy.cs.txt` with `CompensationPolicy.cs`.
The two reviewed edits are the inclusive 60-minute threshold and
explicit rounding. Claim ledger and acceptance suite are unchanged.

| Requirement | Reviewed acceptance case | Result |
|---|---|---|
| FR-001 | Boundary at 0 minutes | PASS |
| FR-001 | Boundary at 59 minutes | PASS |
| FR-002 | Boundary at 60 minutes | PASS |
| FR-002 | Boundary at 119 minutes | PASS |
| FR-003 | Boundary at 120 minutes | PASS |
| FR-004 | Explicit fractional-rappen rounding | PASS |
| FR-004 | Zero fare | PASS |
| FR-004 | Maximum fare and delay | PASS |
| FR-005 | Sequential and concurrent retries | PASS |
| FR-006 | Conflicting reuse leaves original unchanged | PASS |
| FR-007 | Invalid ranges and ID leave ledger empty | PASS |
| FR-007 | Non-integer and missing JSON fields rejected | PASS |

Verification command: `dotnet run --project workspace/app/Claims.Acceptance --configuration Release -- down`.
See the raw logs and JSON reports. The candidate passed the protected
suite; this is not proof of general correctness or a Spec Kit convergence assessment.

Local presenter approval is self-asserted, not enterprise identity verification.
No ADO PR or Foundry deployment was performed by this application.