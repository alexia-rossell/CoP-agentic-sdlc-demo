# Feature specification: delay compensation

**Feature:** `001-delay-compensation` | **Git branch:** `ex_manolito`
**Created:** 2026-10-16 | **Status:** Draft; clarification and approval pending

**Input:** "Update delay compensation for our pilot mobility product: 25% from
60 minutes, 50% from 120 minutes. Use CHF and prevent duplicate compensation."

These are fictional product rules, not Swiss passenger-rights guidance.
An ADO request is the intended source; no actual work item exists yet.

## User scenarios and testing

### US1 - Receive correct compensation (P1)

As a passenger, I receive a predictable amount for an eligible delayed journey.
This is the primary business value and can be tested independently by calculating
compensation for a fare/delay pair.

| Scenario | Fare (rappen) | Delay (minutes) | Expected compensation |
|---|---:|---:|---:|
| Below threshold | 6000 | 59 | 0 |
| First inclusive threshold | 6000 | 60 | 1500 |
| Upper first band | 6000 | 119 | 1500 |
| Second inclusive threshold | 6000 | 120 | 3000 |
| Fractional rappen | 1003 | 60 | Pending FR-004 clarification |
| Zero fare | 0 | 120 | 0 |

### US2 - Retry a claim safely (P2)

As a caller, I can retry without a second compensation-ledger entry.
This preserves a required service invariant and can be tested independently
by submitting the same claim sequentially and concurrently.

1. Given an accepted claim, repeating the same ID and payload returns the original
   result and leaves exactly one ledger entry.
2. Concurrent identical submissions have the same invariant.
3. Reusing an ID with a changed fare or delay returns an explicit conflict and
   leaves the original entry unchanged.

## Requirements

- **FR-001:** Delays below 60 minutes MUST receive zero compensation.
- **FR-002:** Delays from 60 through 119 minutes MUST receive 25% of eligible fare.
- **FR-003:** Delays of 120 minutes or more MUST receive 50% of eligible fare.
- **FR-004:** Monetary inputs/results MUST be integer CHF rappen.
  [NEEDS CLARIFICATION: how should fractional-rappen compensation be rounded?]
- **FR-005:** Identical claim-ID/payload retries MUST return the original result
  with one ledger entry, including concurrent submissions.
- **FR-006:** Conflicting claim-ID reuse MUST fail explicitly without mutation.
- **FR-007:** Negative or non-integer fare/delay inputs MUST fail validation
  without creating a ledger entry. Values outside the documented supported
  numeric range MUST also fail explicitly.

## Entities and edge cases

- **Claim:** non-empty ID, eligible fare in rappen, delay in whole minutes.
- **Result:** claim ID and compensation in rappen.
- **Ledger entry:** the immutable original claim/result for retry comparison.

Test 0, 59, 60, 119 and 120 minutes; zero fare; fractional compensation;
negative/non-integer/out-of-range inputs; sequential/concurrent repeats; and
conflicting reuse. There is no maximum-delay compensation band beyond 50%.

## Scope and assumptions

The demo starts with the prepared .NET service fixture in `app/Claims.Service/`,
including validation and duplicate handling. A new run copies it and injects
labelled defects without changing repository source. The live change remains
the policy calculation. The repository draft stays unresolved; the application
records the chosen rounding decision in each run's separate artifact snapshot.

Real payments, passenger records, authentication products, currency conversion
and legal entitlement decisions are out of scope. No new public API is required.
Human review and automated tests are explicitly required before publication.

## Success criteria

- **SC-001:** Every approved acceptance example produces its specified amount.
- **SC-002:** Sequential and concurrent duplicate scenarios create one entry;
  conflicting/invalid requests create no additional entry.
- **SC-003:** Every functional requirement maps to a reviewed test and real result.
- **SC-004:** Reviewers can identify the approved spec version and verified change
  from the published evidence, with no unresolved clarification at handoff.
