"""azd hooks: read-only preflight, then explicitly approved ADO project creation."""

import argparse
import json
import os
import re
import subprocess
import sys
import time
import uuid
from urllib.error import HTTPError, URLError
from urllib.parse import quote, unquote
from urllib.request import HTTPRedirectHandler, Request, build_opener


ADO_RESOURCE = "499b84ac-1321-427f-aa17-267ca6975798"
MODEL_NAME = "gpt-5.4-mini"
MODEL_VERSION = "2026-03-17"
AGILE_TEMPLATE = "adcc42ab-9882-485e-a3ed-7678f01f66bc"


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise RuntimeError(
            f"Unexpected HTTP {code} redirect from Azure DevOps. "
            "Check that the organization exists and your account has access."
        )


def az_json(*arguments):
    result = subprocess.run(
        ["az", *arguments, "--output", "json", "--only-show-errors"],
        check=True,
        capture_output=True,
        text=True,
        timeout=120,
    )
    return json.loads(result.stdout)


def organization_url(value):
    name = value.removeprefix("https://dev.azure.com/").rstrip("/")
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9-]{0,48}[A-Za-z0-9]|[A-Za-z0-9]", name):
        raise ValueError("ADO_ORGANIZATION must be an organization name or https://dev.azure.com/<name>.")
    return f"https://dev.azure.com/{name}"


def request_json(base, token, path, payload=None, allow_missing=False):
    request = Request(
        f"{base}/_apis/{path}",
        data=None if payload is None else json.dumps(payload).encode("utf-8"),
        headers={
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json",
            "Accept": "application/json",
        },
    )
    try:
        with build_opener(NoRedirect()).open(request, timeout=30) as response:
            if "application/json" not in response.headers.get("Content-Type", "").lower():
                raise RuntimeError("Azure DevOps did not return JSON. Check organization existence, sign-in and tenant membership.")
            return json.load(response)
    except HTTPError as error:
        if allow_missing and error.code == 404:
            return None
        service_error = unquote(error.headers.get("X-TFS-ServiceError", ""))
        if error.code == 403 and "has not been materialized" in service_error.lower():
            raise RuntimeError(
                f"Azure DevOps returned HTTP 403: the Azure CLI identity has not been activated in ADO. "
                f"Open {base}/ in a browser, sign in with the same account shown by "
                "'az account show --subscription <AZURE_SUBSCRIPTION_ID from azd> "
                "--query user.name -o tsv', and complete first-time sign-in. "
                "If the organization is inaccessible, ask its administrator to add that exact account. "
                "Then retry azd provision. Do not create another organization or bypass preflight."
            ) from error
        permission = (
            "Check the organization's Entra tenant and membership for the Azure CLI account."
            if payload is None
            else "Check the organization's Entra tenant, membership and Create new projects permission."
        )
        raise RuntimeError(
            f"Azure DevOps returned HTTP {error.code} for {request.get_method()} {path}. "
            + permission
        ) from error


def check_model(models, usages, sku, capacity):
    available = any(
        item.get("model", {}).get("name") == MODEL_NAME
        and item["model"].get("version") == MODEL_VERSION
        and sku in [entry["name"] for entry in (item["model"].get("skus") or [])]
        for item in models
    )
    if not available:
        raise RuntimeError(f"{MODEL_NAME} {MODEL_VERSION} / {sku} is not available in this region. No model substitution is allowed.")
    usage_name = f"OpenAI.{sku}.{MODEL_NAME}"
    usage = next((item for item in usages if item["name"]["value"] == usage_name), None)
    if usage is None or usage["limit"] - usage["currentValue"] < capacity:
        raise RuntimeError(f"Insufficient or unavailable {usage_name} quota for capacity {capacity}.")


def preflight(base, token):
    subscription = os.environ["AZURE_SUBSCRIPTION_ID"]
    location = os.environ["AZURE_LOCATION"]
    sku = os.environ.get("AZURE_OPENAI_SKU", "GlobalStandard")
    capacity = int(os.environ.get("AZURE_OPENAI_CAPACITY", "10"))
    if sku not in ("GlobalStandard", "DataZoneStandard") or capacity < 1:
        raise ValueError("Use GlobalStandard or DataZoneStandard and a positive integer capacity.")
    request_json(base, token, "projects?$top=1&api-version=7.1")
    models = az_json("cognitiveservices", "model", "list", "--location", location, "--subscription", subscription)
    usages = az_json("cognitiveservices", "usage", "list", "--location", location, "--subscription", subscription)
    check_model(models, usages, sku, capacity)
    print(f"Preflight passed: {base}, {MODEL_NAME} {MODEL_VERSION}, {location}, {sku}, capacity {capacity}.")
    print("Quota is not a capacity reservation. Azure Policy, RBAC and live capacity can still block provisioning.")


def ensure_project(base, token, project_name, approved=False):
    if not project_name.strip():
        raise ValueError("ADO_PROJECT must not be empty.")
    project_path = f"projects/{quote(project_name, safe='')}?api-version=7.1"
    project = request_json(base, token, project_path, allow_missing=True)
    if project is not None:
        if project.get("state") != "wellFormed":
            raise RuntimeError(f"Existing project is not ready: {project.get('state')}. Retry after it is ready.")
        print(f"Reusing Azure DevOps project: {base}/{quote(project_name, safe='')}")
        return
    if not approved:
        if not sys.stdin.isatty():
            raise RuntimeError("Project creation needs interactive approval. Run azd up interactively, or invoke ensure-project --approve-creation explicitly.")
        answer = input(f"Create PRIVATE Git/Agile project '{project_name}' in {base}? Type 'create': ")
        if answer != "create":
            raise RuntimeError("Azure DevOps project creation was not approved.")
    operation = request_json(
        base,
        token,
        "projects?api-version=7.1",
        {
            "name": project_name,
            "description": "Synthetic agentic SDLC demo",
            "visibility": "private",
            "capabilities": {
                "versioncontrol": {"sourceControlType": "Git"},
                "processTemplate": {"templateTypeId": AGILE_TEMPLATE},
            },
        },
    )
    operation_id = str(uuid.UUID(operation["id"]))
    for _ in range(90):
        status = request_json(base, token, f"operations/{operation_id}?api-version=7.1")
        if status["status"] == "succeeded":
            project = request_json(base, token, project_path)
            if project.get("state") != "wellFormed":
                raise RuntimeError("Project operation succeeded but the project is not ready. Retry azd up.")
            print(f"Created Azure DevOps project: {base}/{quote(project_name, safe='')}")
            return
        if status["status"] in ("failed", "cancelled"):
            raise RuntimeError(f"Azure DevOps project creation {status['status']}: {status.get('resultMessage') or status.get('detailedMessage')}")
        time.sleep(2)
    raise TimeoutError(f"Timed out waiting for ADO operation {operation_id}. Check the organization before retrying azd up.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["preflight", "ensure-project"])
    parser.add_argument("--approve-creation", action="store_true", help="Explicit approval for this invocation's ADO project creation.")
    args = parser.parse_args()
    base = organization_url(os.environ.get("ADO_ORGANIZATION", "swissreghcpsdkdemo"))
    subscription = os.environ["AZURE_SUBSCRIPTION_ID"]
    account = az_json("account", "show", "--subscription", subscription)
    print(
        f"ADO hook identity: {account['user']['name']}; tenant: {account['tenantId']}; "
        f"azd subscription: {account['id']}. The Azure CLI default subscription is not used.",
        flush=True,
    )
    token = az_json(
        "account", "get-access-token", "--resource", ADO_RESOURCE,
        "--subscription", subscription,
    )["accessToken"]
    if args.command == "preflight":
        preflight(base, token)
    else:
        ensure_project(base, token, os.environ.get("ADO_PROJECT", "CoP-agentic-sdlc-demo"), args.approve_creation)


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as error:
        print(f"ERROR: Azure CLI failed: {error.stderr.strip()}", file=sys.stderr)
        sys.exit(1)
    except (RuntimeError, ValueError, KeyError, OSError, URLError, subprocess.TimeoutExpired) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        sys.exit(1)
