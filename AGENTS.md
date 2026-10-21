# Repository guidance

- Small .NET-only ADO/Copilot SDK demo; keep the current Git branch.
- Ticket intake, one explicit human clarification, approval, diagnosis/repair,
  verification and separately approved ADO publication are the whole story.
  Do not add specification-generation tooling or a multi-agent pipeline.
- Use integer rappen. Preserve the acceptance runner, validation, duplicate
  handling and supported ranges. Repository claims code is correct; seed the
  single demonstration defect only in a new run's workspace.
- The offline ticket fixture keeps rounding unresolved. Collect the actual
  product-owner answer and explicit approval per run. Never pre-fill approvals.
- The SDK must infer the fix from ticket/source/failing tests; do not supply
  exact replacement expressions in its prompt. Enforce the expression grammar,
  protected-file hashes, permission checks, timeouts and tool budgets in code.
- Live ADO uses environment configuration. Remote writes need explicit approval
  and revision checks. Do not push code, merge, or deploy implicitly.
- Focus on the Copilot SDK controller, ADO integration and Foundry deployment
  via the official .NET Invocations library. Keep setup separate from the
  short execution workflow; do not add presentation tooling.
- Build with `dotnet build Demo.slnx`; test with
  `dotnet test tests/Demo.Tests/Demo.Tests.csproj`.
- Local SDK integration tests use a scripted provider and ADO tests use fake
  HTTP. Report actual checks/errors; never claim real-model inference, ADO
  connectivity, cloud deployment or telemetry from local tests.
