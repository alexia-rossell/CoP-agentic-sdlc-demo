import contextlib
import io
import json
from pathlib import Path
import subprocess
import tempfile
from unittest import TestCase, main
from unittest.mock import patch

import provision


class ProvisionTests(TestCase):
    def deploy(self, existing):
        env = {
            "AZURE_SUBSCRIPTION_ID": "subscription",
            "AZURE_RESOURCE_GROUP": "demo-rg",
            "AZURE_FOUNDRY_PROJECT_ENDPOINT": "https://demo.services.ai.azure.com/api/projects/demo",
            "AZURE_FOUNDRY_ACCOUNT_NAME": "demo",
            "AZURE_FOUNDRY_PROJECT_NAME": "demo",
            "AZURE_LOCATION": "swedencentral",
            "ADO_ORGANIZATION": "demo-org",
            "ADO_PROJECT": "demo",
            "AZURE_OPENAI_DEPLOYMENT_NAME": "gpt-5.4-mini",
            "AZURE_OPENAI_ENDPOINT": "https://demo.openai.azure.com",
        }
        azure_calls = []
        rest_calls = []

        def azure(*args):
            azure_calls.append(args)
            if args[:4] == ("cognitiveservices", "account", "project", "show"):
                return {"identity": {"principalId": "project-principal"}}
            if args[:3] == ("deployment", "group", "create"):
                return {"properties": {"outputs": {"applicationInsightsId": {"value": "/resources/appinsights"}}}}
            return None

        def rest(method, url, resource="https://management.azure.com/", body=None):
            rest_calls.append((method, url, resource, body))
            if "/endpoint/sessions?" in url:
                return {"agent_session_id": "new-session"}
            if method == "get":
                return {"status": "active"}
            return {"version": "3"}

        with tempfile.TemporaryDirectory() as directory:
            state = Path(directory) / "hosted-demo.json"
            if existing:
                state.write_text(json.dumps({
                    "status": "active", "session": "old-session", "subscription": "subscription",
                    "endpoint": env["AZURE_FOUNDRY_PROJECT_ENDPOINT"] + "/agents/existing",
                    "agent": "existing",
                }))
            with patch.object(provision, "STATE", state), \
                    patch.object(provision, "SECRET", Path(directory) / "approval"), \
                    patch.object(provision, "azure", side_effect=azure), \
                    patch.object(provision, "rest", side_effect=rest), \
                    patch.object(provision.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, json.dumps(env))), \
                    patch("sys.argv", ["provision.py", "--approve", "--update"]), \
                    contextlib.redirect_stdout(io.StringIO()):
                provision.main()
            result = json.loads(state.read_text())
        self.assertEqual(result["applicationInsightsId"], "/resources/appinsights")
        self.assertEqual(result["session"], "new-session")
        self.assertEqual(result["status"], "active")
        deployment = next(args for args in azure_calls if args[:3] == ("deployment", "group", "create"))
        self.assertIn("accountName=demo", deployment)
        self.assertIn("projectName=demo", deployment)
        create = next(call for call in rest_calls if call[0] == "post" and "/endpoint/sessions?" not in call[1])
        variables = create[3]["definition"]["environment_variables"]
        self.assertEqual(variables["OTEL_SERVICE_NAME"], "demo-agent")
        self.assertEqual(variables["OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT"], "false")
        self.assertNotIn("APPLICATIONINSIGHTS_CONNECTION_STRING", variables)
        self.assertNotIn("DEMO_MODEL_API_KEY", variables)
        return create

    def test_azd_update_can_create_initial_agent_with_tracing(self):
        create = self.deploy(existing=False)
        self.assertTrue(create[1].endswith("/agents?api-version=v1"))
        self.assertIn("name", create[3])

    def test_azd_update_redeploys_existing_agent_with_tracing(self):
        create = self.deploy(existing=True)
        self.assertTrue(create[1].endswith("/agents/existing/versions?api-version=v1"))
        self.assertNotIn("name", create[3])


if __name__ == "__main__":
    main()
