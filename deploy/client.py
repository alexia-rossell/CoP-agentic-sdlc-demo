"""Invoke the deployed controller; Azure CLI refreshes delegated tokens per call."""

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
from urllib.error import HTTPError
from urllib.parse import urlencode, urlparse
from urllib.request import HTTPRedirectHandler, Request, build_opener


ROOT = Path(__file__).resolve().parents[1]
STATE = ROOT / ".azure" / "hosted-demo.json"
SECRET = ROOT / ".azure" / "hosted-approval-token"


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise RuntimeError("Refusing to forward credentials across an HTTP redirect.")


def azure(*args):
    result = subprocess.run(
        ["az", *args, "--only-show-errors", "--output", "json"],
        capture_output=True, text=True, check=True,
    )
    return json.loads(result.stdout) if result.stdout.strip() else None


def token(resource, subscription):
    return azure("account", "get-access-token", "--resource", resource,
                 "--subscription", subscription)["accessToken"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("args", nargs=argparse.REMAINDER)
    args = parser.parse_args().args
    if len(args) < 2:
        parser.error("Supply the same command and run ID as the local agent, e.g. status live1.")
    settings = json.loads(STATE.read_text())
    endpoint = urlparse(settings["endpoint"])
    if (endpoint.scheme != "https" or not endpoint.hostname
            or not endpoint.hostname.endswith(".services.ai.azure.com")
            or endpoint.username or endpoint.query or endpoint.fragment):
        raise ValueError("Invalid Foundry endpoint in the local deployment record.")
    if not settings.get("session"):
        raise ValueError("The deployment has no recorded session. Inspect deployment status before invoking it.")
    headers = {
        "Authorization": "Bearer " + token("https://ai.azure.com", settings["subscription"]),
        "Content-Type": "application/json",
        "Accept": "application/json",
        "X-Client-Demo-Approval": SECRET.read_text().strip(),
    }
    offline_commands = {"init-offline", "clarify", "status", "export"}
    if args[0] not in offline_commands:
        headers["X-Client-Demo-Ado-Token"] = token("499b84ac-1321-427f-aa17-267ca6975798", settings["subscription"])
    if args[0] == "execute" and args[-1] == "sdk":
        headers["X-Client-Demo-Model-Token"] = token("https://cognitiveservices.azure.com", settings["subscription"])
    url = settings["endpoint"] + "/endpoint/protocols/invocations?" + urlencode({
        "api-version": "v1", "agent_session_id": settings["session"],
    })
    request = Request(url, data=json.dumps({"args": args}).encode(), headers=headers)
    try:
        with build_opener(NoRedirect()).open(request, timeout=600) as response:
            print(json.dumps(json.load(response), indent=2))
    except HTTPError as error:
        print(f"Invocation failed (HTTP {error.code}): {error.read().decode()}", file=sys.stderr)
        print("Do not blindly retry a remote-write command; inspect ADO and the same session first.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except subprocess.CalledProcessError as error:
        print(f"Azure CLI failed: {error.stderr.strip()}", file=sys.stderr)
        sys.exit(1)
    except (OSError, ValueError, RuntimeError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        sys.exit(1)
