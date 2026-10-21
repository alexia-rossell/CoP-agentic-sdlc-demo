import contextlib
import io
import json
from unittest import TestCase, main
from unittest.mock import MagicMock, patch

import client


class ClientTests(TestCase):
    def invoke(self, args):
        settings = {
            "subscription": "subscription", "session": "session-1",
            "endpoint": "https://demo.services.ai.azure.com/api/projects/demo/agents/demo",
        }
        state = MagicMock()
        state.read_text.return_value = json.dumps(settings)
        secret = MagicMock()
        secret.read_text.return_value = "presenter-secret"
        response = MagicMock()
        response.read.return_value = '{"state":{"stage":"Draft"}}'
        opener = MagicMock()
        opener.open.return_value.__enter__.return_value = response
        with patch.object(client, "STATE", state), patch.object(client, "SECRET", secret), \
                patch.object(client, "token", side_effect=lambda resource, subscription: "token:" + resource) as token, \
                patch.object(client, "build_opener", return_value=opener), \
                patch("sys.argv", ["client.py", *args]), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(client.main(), 0)
        return opener.open.call_args.args[0], token

    def test_reuses_session_and_posts_exact_command(self):
        request, token = self.invoke(["status", "live1"])
        self.assertIn("agent_session_id=session-1", request.full_url)
        self.assertEqual(json.loads(request.data), {"args": ["status", "live1"]})
        self.assertEqual(token.call_count, 1)

    def test_ado_credential_is_request_scoped(self):
        request, token = self.invoke(["check", "live1"])
        self.assertIn("X-client-demo-ado-token", request.headers)
        self.assertNotIn("X-client-demo-model-token", request.headers)
        self.assertEqual(token.call_count, 2)

    def test_sdk_execution_gets_fresh_model_credential(self):
        request, token = self.invoke(["execute", "live1", "--mode", "sdk"])
        self.assertIn("X-client-demo-model-token", request.headers)
        self.assertEqual(token.call_count, 3)
        self.assertNotIn("token:", request.data.decode())

    def test_redirects_are_rejected(self):
        with self.assertRaises(RuntimeError):
            client.NoRedirect().redirect_request(None, None, 302, "", {}, "https://other.example")


if __name__ == "__main__":
    main()
