# Repository guidance

- This is a .NET-only SDD/Copilot SDK demo. The claims fixture and local SDK
  workflow are implemented; live ADO/Foundry integrations are not.
- Read `.specify/memory/constitution.md` and the active feature's spec, plan and
  tasks before implementation. Spec Kit Copilot skills are in `.github/skills/`.
- The active feature is `001-delay-compensation`; keep the existing Git branch.
  Do not create another feature just to continue the prepared walkthrough.
- The unresolved business spec is presentation input, not a blocker to developing
  the demo. Keep it unresolved in the repository; collect explicit per-run
  rounding and approval through the application. Never pre-fill approval.
- Use integer rappen. Preserve reviewed acceptance criteria and regression tests.
- Stop for approval before contract changes or remote writes. These instructions
  guide behaviour; runtime/tool controls must enforce permissions separately.
- Report actual checks and errors. Do not fabricate test results, approvals,
  ADO links, deployment status or telemetry.
- Build with `dotnet build Demo.slnx`; test with
  `dotnet test tests/Demo.Tests/Demo.Tests.csproj`. The SDK integration test uses
  a local scripted provider, not a real model. Offline rehearsal is labelled.
