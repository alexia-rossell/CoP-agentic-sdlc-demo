#!/usr/bin/env bash
set -euo pipefail

deck="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd -- "$deck/../.." && pwd)"
cd "$root"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

run_id="${1:-fallback-20261016}"
if [[ -e ".demo/runs/$run_id" ]]; then
    printf 'Run already exists: %s. Supply a fresh run ID; captures are never silently reset.\n' "$run_id" >&2
    exit 1
fi

mkdir -p "$deck/captured"
dotnet build Demo.slnx --no-restore > "$deck/captured/build.log" 2>&1
cli=(dotnet run --no-build --project agent/Demo.Agent --)
"${cli[@]}" init "$run_id" | tee "$deck/captured/01-init.log"
"${cli[@]}" clarify "$run_id" --rounding down | tee "$deck/captured/02-clarify.log"
"${cli[@]}" approve "$run_id" --by presenter | tee "$deck/captured/03-approve.log"
"${cli[@]}" execute "$run_id" --mode rehearsal | tee "$deck/captured/04-execute.log"
"${cli[@]}" status "$run_id" > "$deck/captured/05-status.log"
cp ".demo/runs/$run_id/state.json" "$deck/captured/05-status.json"
"${cli[@]}" export "$run_id" --by reviewer | tee "$deck/captured/06-export.log"
cp ".demo/runs/$run_id/export/review.md" "$deck/captured/review.md"
cp ".demo/runs/$run_id/export/evidence.json" "$deck/captured/evidence.json"
cp ".demo/runs/$run_id/export/baseline-results.json" "$deck/captured/baseline-results.json"
cp ".demo/runs/$run_id/export/candidate-results.json" "$deck/captured/candidate-results.json"
cp ".demo/runs/$run_id/export/before-policy.cs.txt" "$deck/captured/before-policy.cs.txt"
cp ".demo/runs/$run_id/export/CompensationPolicy.cs" "$deck/captured/after-policy.cs.txt"
cp ".demo/runs/$run_id/export/spec.md" "$deck/captured/resolved-spec.md"
dotnet test tests/Demo.Tests/Demo.Tests.csproj --no-build --no-restore \
    --filter FullyQualifiedName~SdkIntegrationTests \
    --logger 'console;verbosity=normal' > "$deck/captured/sdk-runtime-test.log" 2>&1
dotnet publish agent/Demo.Agent -c Release -o .demo/publish > "$deck/captured/publish.log" 2>&1
dotnet .demo/publish/Demo.Agent.dll --help > "$deck/captured/published-help.log"
published=(dotnet .demo/publish/Demo.Agent.dll)
{
    "${published[@]}" init "$run_id-pkg"
    "${published[@]}" clarify "$run_id-pkg" --rounding down
    "${published[@]}" approve "$run_id-pkg" --by presenter
    "${published[@]}" execute "$run_id-pkg" --mode rehearsal
} > "$deck/captured/packaged-run.log" 2>&1
printf 'Captured local rehearsal %s and real SDK/runtime test with a scripted local provider.\n' "$run_id"
printf 'Slides contain selected verbatim excerpts; review them after recapturing.\n'
