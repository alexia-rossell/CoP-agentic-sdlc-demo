"""Build and deploy one simple Foundry controller. Never creates ADO work items."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
STATE = ROOT / ".azure" / "hosted-demo.json"
SECRET = ROOT / ".azure" / "hosted-approval-token"


def azure(*args):
    result = subprocess.run(
        ["az", *args, "--only-show-errors", "--output", "json"],
        capture_output=True, text=True, check=True,
    )
    return json.loads(result.stdout) if result.stdout.strip() else None


def rest(method, url, resource="https://management.azure.com/", body=None):
    args = ["rest", "--method", method, "--url", url, "--resource", resource]
    if body is None:
        return azure(*args)
    with tempfile.NamedTemporaryFile(mode="w", suffix=".json") as payload:
        json.dump(body, payload)
        payload.flush()
        return azure(*args, "--headers", "Content-Type=application/json", "--body", "@" + payload.name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--approve", action="store_true")
    parser.add_argument("--update", action="store_true", help="Deploy a fresh version of the recorded active agent; uses a new session.")
    args = parser.parse_args()
    recorded = None
    if STATE.exists():
        recorded = json.loads(STATE.read_text())
        if recorded.get("status") == "active" and recorded.get("session") and not args.update:
            print("Reusing recorded active deployment:", recorded["endpoint"])
            return
        if recorded.get("status") != "active":
            raise RuntimeError(f"{STATE} records an incomplete deployment. Inspect it; do not blindly create another agent.")
    if not args.approve:
        if not sys.stdin.isatty() or input("Build/upload image and create billable Foundry hosted compute? Type 'deploy': ") != "deploy":
            raise RuntimeError("Deployment was not approved.")
    env = json.loads(subprocess.run(
        ["azd", "env", "get-values", "--output", "json"], check=True, capture_output=True, text=True,
    ).stdout)
    subscription = env["AZURE_SUBSCRIPTION_ID"]
    resource_group = env["AZURE_RESOURCE_GROUP"]
    project_endpoint = env["AZURE_FOUNDRY_PROJECT_ENDPOINT"]
    account = env["AZURE_FOUNDRY_ACCOUNT_NAME"]
    project = env["AZURE_FOUNDRY_PROJECT_NAME"]
    if recorded is not None and (
        recorded["subscription"] != subscription
        or recorded["endpoint"] != project_endpoint + "/agents/" + recorded["agent"]
    ):
        raise RuntimeError("The recorded deployment and azd environment target differ. Review them before updating.")
    azure("account", "set", "--subscription", subscription)
    suffix = hashlib.sha256(project_endpoint.encode()).hexdigest()[:12]
    registry = "acrdemo" + suffix
    updating = args.update and recorded is not None
    agent = recorded["agent"] if updating else "ado-demo-" + time.strftime("%Y%m%d%H%M%S", time.gmtime())
    tag = time.strftime("%Y%m%d%H%M%S", time.gmtime())
    principal = azure("cognitiveservices", "account", "project", "show", "--name", account,
                      "--project-name", project, "--resource-group", resource_group)["identity"]["principalId"]
    infrastructure = azure("deployment", "group", "create", "--name", "hosted-demo",
          "--resource-group", resource_group, "--template-file", str(ROOT / "infra/hosted.bicep"),
          "--parameters", f"location={env['AZURE_LOCATION']}", f"registryName={registry}",
          f"projectPrincipalId={principal}", f"accountName={account}", f"projectName={project}")
    SECRET.parent.mkdir(parents=True, exist_ok=True)
    if not SECRET.exists():
        descriptor = os.open(SECRET, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w") as output:
            output.write(secrets.token_urlsafe(48))
    project_id = f"/subscriptions/{subscription}/resourceGroups/{resource_group}/providers/Microsoft.CognitiveServices/accounts/{account}/projects/{project}"
    rest("put", "https://management.azure.com" + project_id + "/connections/demo-presenter?api-version=2025-06-01", body={
        "properties": {
            "authType": "CustomKeys", "category": "CustomKeys", "target": "demo-presenter",
            "credentials": {"keys": {"approval_token": SECRET.read_text().strip()}},
        },
    })
    state = {"subscription": subscription, "registry": registry, "agent": agent,
             "endpoint": project_endpoint + "/agents/" + agent, "imageTag": tag,
             "status": "building", "session": None,
             "applicationInsightsId": infrastructure["properties"]["outputs"]["applicationInsightsId"]["value"]}
    STATE.write_text(json.dumps(state, indent=2))
    print(f"Building {registry}.azurecr.io/ado-demo:{tag}", flush=True)
    subprocess.run(
        ["az", "acr", "build", "--registry", registry, "--image", "ado-demo:" + tag,
         "--file", "deploy/Dockerfile", "--platform", "linux/amd64", "."],
        cwd=ROOT, check=True,
    )
    state["status"] = "creating-agent"
    STATE.write_text(json.dumps(state, indent=2))
    payload = {
        "name": agent,
        "definition": {
            "kind": "hosted",
            "container_configuration": {"image": f"{registry}.azurecr.io/ado-demo:{tag}"},
            "cpu": "1", "memory": "2Gi",
            "protocol_versions": [{"protocol": "invocations", "version": "2.0.0"}],
            "environment_variables": {
                "ADO_ORGANIZATION": env["ADO_ORGANIZATION"], "ADO_PROJECT": env["ADO_PROJECT"],
                "DEMO_MODEL": env["AZURE_OPENAI_DEPLOYMENT_NAME"],
                "DEMO_MODEL_ENDPOINT": env["AZURE_OPENAI_ENDPOINT"].rstrip("/") + "/openai/v1",
                "DEMO_PROVIDER": "openai", "DEMO_WIRE_API": "completions",
                "DEMO_APPROVAL_TOKEN": "${{connections.demo-presenter.credentials.approval_token}}",
                "OTEL_SERVICE_NAME": "demo-agent",
                "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT": "false",
            },
        },
    }
    create_url = project_endpoint + "/agents?api-version=v1"
    if updating:
        create_url = project_endpoint + "/agents/" + agent + "/versions?api-version=v1"
        payload.pop("name")
    created = rest("post", create_url, "https://ai.azure.com", payload)
    state["version"] = str(created.get("version", "1"))
    STATE.write_text(json.dumps(state, indent=2))
    for _ in range(120):
        version = rest("get", state["endpoint"] + "/versions/" + state["version"] + "?api-version=v1", "https://ai.azure.com")
        status = str(version.get("status", "")).lower()
        print("Foundry version status:", status, flush=True)
        if status == "active":
            session = rest("post", state["endpoint"] + "/endpoint/sessions?api-version=v1",
                           "https://ai.azure.com", {})
            state.update(status="active", session=session["agent_session_id"])
            STATE.write_text(json.dumps(state, indent=2))
            print("Deployment active. Run: dotnet run --project agent/Demo.Agent -- remote init-offline cloud-smoke")
            return
        if status in ("failed", "deleted", "deleting"):
            state["status"] = status
            STATE.write_text(json.dumps(state, indent=2))
            raise RuntimeError(f"Foundry deployment failed: {version.get('error', status)}")
        time.sleep(5)
    raise TimeoutError("Foundry did not become active in ten minutes. Inspect recorded deployment; do not recreate blindly.")


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as error:
        print(f"Azure command failed: {error.stderr or error}", file=sys.stderr)
        sys.exit(1)
    except (OSError, ValueError, KeyError, RuntimeError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        sys.exit(1)
