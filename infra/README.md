# Azure infrastructure with `azd up`

One `azd up` invocation provisions the infrastructure and, with separate
postdeploy approval, deploys the hosted controller:

- A resource group in the selected subscription, tagged for the azd environment.
- A Microsoft Foundry `AIServices` account with managed identity and a project.
- An account-level `gpt-5.4-mini` model deployment, version `2026-03-17`,
  available to that Foundry project. No other model is substituted.
- Cognitive Services OpenAI User access for the azd principal.
- A private Git/Agile Azure DevOps project in `swissreghcpsdkdemo`, through the
  postprovision hook. Existing ready projects are reused without changes.
- A Basic container registry, image build and single-session Foundry controller.

It does **not** deploy a standalone claims service, telemetry, a gateway, or a
pipeline, and does not change the demo's per-run business approval flow.

## One-time prerequisites

- Azure CLI (`az`), Azure Developer CLI (`azd`), Bicep CLI and Python 3.9+.
  Python is only used for deployment hooks; the application remains .NET-only.
  Run `az bicep install` if Bicep is missing.
- `az login` and `azd auth login` in the same Entra tenant.
- Permission to create resource groups and Cognitive Services resources and
  assign roles (for example Contributor plus Role Based Access Control
  Administrator at the appropriate scope). Register `Microsoft.CognitiveServices`
  in the subscription if needed; registration requires its own permission.
- Azure DevOps organization membership and **Create new projects** permission.
  Hook authentication uses the signed-in Azure CLI principal's short-lived
  Entra token, never a checked-in PAT.

**Organization creation is the one manual step.** Microsoft explicitly states
that [automated Azure DevOps organization creation is not supported][ado-org].
If `swissreghcpsdkdemo` does not exist, create it in the Azure DevOps portal,
choose its hosting geography, connect the correct Entra tenant and select the
subscription for billing. Azure DevOps organizations/projects are not ARM
resources inside the resource group. No undocumented organization API is used.

## Configure once, then run `azd up`

From the repository root:

```bash
azd env new cop-agentic-demo \
  --subscription <your-target-subscription-id> \
  --location swedencentral
azd env set ADO_ORGANIZATION swissreghcpsdkdemo
azd env set ADO_PROJECT CoP-agentic-sdlc-demo
azd env set AZURE_OPENAI_SKU GlobalStandard
azd env set AZURE_OPENAI_CAPACITY 10

azd up
```

Skip `env new` if this local environment is already configured. Environment state
and outputs live in ignored `.azure/`; no credentials are emitted by Bicep.

The **preprovision** hook verifies organization access, the exact model/version
and SKU in the region, and remaining subscription quota before Azure provisioning.
It fails on inaccessible/missing organizations, authentication errors, unavailable
models or insufficient quota. It performs no remote writes.

The **postprovision** hook asks you to type `create` before creating a missing ADO
project. No approval is pre-filled. Run interactively; `azd --no-prompt` cannot
approve this independent remote write. For an explicitly approved noninteractive
project-creation invocation, export the same subscription/ADO environment values
and run:

```bash
python3 infra/scripts/ado_project.py ensure-project --approve-creation
```

This flag authorizes that invocation only; it is not stored in azd environment
state. Normal use requires only `azd up`, not a separate provisioning command.

If the ADO hook fails after Azure provisioning, `azd up` reports failure but the
Azure resources remain. Correct access/approve creation and rerun `azd up`;
ready ADO projects are reused. Do not change project names to work around an
in-progress or failed creation without inspecting the organization first.

### ADO HTTP 403: identity has not been materialized

If preflight reports that the Azure CLI identity has not been activated in ADO:

1. Check the **azd environment's subscription**, not just the CLI default:
   ```bash
   azd env get-value AZURE_SUBSCRIPTION_ID
   az account show --subscription <subscription-id-from-above> \
     --query '{subscription:id,tenant:tenantId,user:user.name}'
   ```
   The hook explicitly uses that subscription to acquire the ADO token and
   prints its account/tenant before making the request. `az account set` alone
   does not update the subscription persisted in the azd environment.
2. Open <https://dev.azure.com/swissreghcpsdkdemo/> in a browser and sign in with
   **that same account**, not a different account already signed into the browser.
   Complete any first-time Azure DevOps profile/sign-in prompts.
3. If access is still denied, ask the organization administrator to add the
   exact CLI account under **Organization settings > Users**, and confirm it
   belongs to the organization's connected Entra tenant.
4. Retry `azd provision`. The read-only preflight must pass before provisioning.

This activation error is not proof that Create new projects permission is
missing. That separate permission is needed only when creating a project.
The azd update notice is unrelated; do not bypass preflight or create another
organization to work around the sign-in requirement.

If the stored subscription is wrong, explicitly select the intended target:

```bash
azd env set AZURE_SUBSCRIPTION_ID <intended-subscription-id>
az account show --subscription <intended-subscription-id> \
  --query '{subscription:id,tenant:tenantId,user:user.name}'
azd auth login --tenant-id <tenant-id-from-above>
```

This changes the target for Azure provisioning, not merely ADO authentication.
If the environment already owns resources, review those resources before changing
its subscription; use a separate azd environment for a distinct deployment.
The selected account must still have ADO organization access. Then retry
`azd provision`; quota and model availability will be rechecked in that subscription.

## Model, cost and network choices

The live Azure catalog for this subscription in `swedencentral` reported
`gpt-5.4-mini`, version `2026-03-17`, and GlobalStandard quota of 1,000 units
(0 used) during preparation. This is not a deployment or capacity guarantee.
The hook rechecks quota/catalog at deployment time.

The preparation-time read-only ADO preflight returned **HTTP 404** for
`swissreghcpsdkdemo`. This can mean the organization is missing or the signed-in
identity cannot access it; it does not establish which. Resolve organization
creation/access and tenant membership before running `azd up`. The hook stops
before Azure provisioning when this check fails.

Default capacity is 10 deployment units; these are Azure SKU quota units, not
10 tokens/minute. Check current model-specific rate limits and pay-as-you-go
token pricing before sending traffic. No provisioned-throughput SKU is used.
Model upgrades are pinned (`NoAutoUpgrade`); review and update both Bicep and
preflight validation deliberately if that version is retired.

**GlobalStandard may process inference globally**, even though the account is
in Sweden. If EU data-zone processing is required, explicitly choose:

```bash
azd env set AZURE_OPENAI_SKU DataZoneStandard
azd up
```

The preflight verifies quota for the chosen SKU. Regional resource placement
alone is not a guarantee of inference data residency.

The demo uses public HTTPS endpoints with **key authentication disabled** and
Entra/RBAC authentication required. It does not deploy private endpoints/VNet
isolation. Subscription Azure Policy may require a different reviewed network
design. Role assignments can take time to propagate.

`azd env get-values` shows non-secret endpoints and deployment outputs after a
successful provision.

### One simple hosted controller

The [hosted-agent deployment](../deploy/README.md) is now invoked by the
`azd up` postdeploy hook, with its own deployment approval prompt. It adds an ACR,
cloud image build, presenter-secret connection and one hosted controller/session.
Model key authentication stays **disabled**. The thin client obtains fresh
delegated Entra tokens on every invocation; the hosted process does not store
PATs or long-lived model credentials. No business decision or workflow approval
is pre-filled.

This deliberately simple deployment has a documented single-session limitation,
not a custom durable storage layer. See the hosting guide before using reset or
recovering from an interrupted invocation.

## Validation and teardown

```bash
az bicep build --file infra/main.bicep --stdout > /tmp/cop-agentic-main.json
python3 -B -m unittest discover -s infra/scripts -p 'test_*.py'
```

The tests mock CLI/HTTP behavior; they do not create Azure/ADO resources or prove
that Azure will admit a live deployment.

`azd down` removes the Azure deployment resources, subject to its confirmation.
**It does not delete the ADO project or organization**; they can contain work
items and repositories and must be reviewed/deleted separately in Azure DevOps.
No automatic ADO deletion hook is included.

[ado-org]: https://learn.microsoft.com/en-us/azure/devops/organizations/accounts/create-organization?view=azure-devops
