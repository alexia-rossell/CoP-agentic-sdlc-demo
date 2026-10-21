import io
import unittest
from unittest.mock import patch
from urllib.error import HTTPError

import ado_project as hooks


BASE = "https://dev.azure.com/example"
READY = {"state": "wellFormed"}
OPERATION_ID = "066488b8-b14e-43d1-befc-a2e655266e2b"


class DeploymentHookTests(unittest.TestCase):
    @patch.dict("os.environ", {"AZURE_SUBSCRIPTION_ID": "configured-subscription"})
    @patch("ado_project.sys.argv", ["ado_project.py", "preflight"])
    @patch("ado_project.preflight")
    @patch("ado_project.az_json", side_effect=[
        {"id": "configured-subscription", "tenantId": "configured-tenant", "user": {"name": "configured-user"}},
        {"accessToken": "synthetic-token"},
    ])
    def test_identity_and_token_use_azd_subscription_not_cli_default(self, azure, preflight):
        with patch("builtins.print") as output:
            hooks.main()
        self.assertEqual(
            azure.call_args_list[0].args,
            ("account", "show", "--subscription", "configured-subscription"),
        )
        self.assertEqual(
            azure.call_args_list[1].args,
            ("account", "get-access-token", "--resource", hooks.ADO_RESOURCE,
             "--subscription", "configured-subscription"),
        )
        self.assertIn("configured-user", output.call_args.args[0])
        self.assertIn("configured-tenant", output.call_args.args[0])
        self.assertNotIn("synthetic-token", output.call_args.args[0])
        self.assertEqual(preflight.call_args.args[1], "synthetic-token")

    def test_organization_is_restricted_to_dev_azure_com(self):
        self.assertEqual(hooks.organization_url("example"), BASE)
        self.assertEqual(hooks.organization_url(BASE + "/"), BASE)
        for value in ("https://other.example", "example/other", "", "example?query"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                hooks.organization_url(value)

    def test_exact_model_version_sku_and_quota(self):
        models = [{"model": {"name": hooks.MODEL_NAME, "version": hooks.MODEL_VERSION, "skus": [{"name": "GlobalStandard"}]}}]
        usages = [{"name": {"value": "OpenAI.GlobalStandard.gpt-5.4-mini"}, "limit": 10, "currentValue": 0}]
        hooks.check_model(models, usages, "GlobalStandard", 10)
        for catalog, quota, sku, capacity in (
            ([], usages, "GlobalStandard", 10),
            (models, [], "GlobalStandard", 10),
            (models, usages, "DataZoneStandard", 10),
            (models, usages, "GlobalStandard", 11),
        ):
            with self.subTest(sku=sku, capacity=capacity), self.assertRaises(RuntimeError):
                hooks.check_model(catalog, quota, sku, capacity)
        models[0]["model"]["version"] = "wrong-version"
        with self.assertRaises(RuntimeError):
            hooks.check_model(models, usages, "GlobalStandard", 10)

    @patch("ado_project.request_json", return_value=READY)
    def test_existing_project_does_not_prompt_or_write(self, request):
        hooks.ensure_project(BASE, "token", "demo")
        request.assert_called_once_with(BASE, "token", "projects/demo?api-version=7.1", allow_missing=True)

    @patch("ado_project.request_json", return_value={"state": "deleting"})
    def test_unready_project_fails(self, request):
        with self.assertRaisesRegex(RuntimeError, "not ready"):
            hooks.ensure_project(BASE, "token", "demo", approved=True)
        self.assertEqual(request.call_count, 1)

    @patch("ado_project.sys.stdin.isatty", return_value=False)
    @patch("ado_project.request_json", return_value=None)
    def test_noninteractive_missing_project_never_writes(self, request, isatty):
        with self.assertRaisesRegex(RuntimeError, "interactive approval"):
            hooks.ensure_project(BASE, "token", "demo")
        self.assertEqual(request.call_count, 1)

    @patch("ado_project.sys.stdin.isatty", return_value=True)
    @patch("builtins.input", return_value="no")
    @patch("ado_project.request_json", return_value=None)
    def test_declined_approval_never_writes(self, request, answer, isatty):
        with self.assertRaisesRegex(RuntimeError, "not approved"):
            hooks.ensure_project(BASE, "token", "demo")
        self.assertEqual(request.call_count, 1)

    @patch("ado_project.time.sleep")
    @patch("ado_project.request_json", side_effect=[
        None, {"id": OPERATION_ID}, {"status": "inProgress"}, {"status": "succeeded"}, READY,
    ])
    def test_creation_is_private_git_agile_and_waits_for_completion(self, request, sleep):
        hooks.ensure_project(BASE, "token", "demo", approved=True)
        payload = request.call_args_list[1].args[3]
        self.assertEqual(payload["visibility"], "private")
        self.assertEqual(payload["capabilities"]["versioncontrol"]["sourceControlType"], "Git")
        self.assertEqual(payload["capabilities"]["processTemplate"]["templateTypeId"], hooks.AGILE_TEMPLATE)
        self.assertEqual(request.call_count, 5)
        sleep.assert_called_once_with(2)

    @patch("ado_project.request_json", side_effect=[
        None, {"id": OPERATION_ID}, {"status": "failed", "resultMessage": "permission denied"},
    ])
    def test_async_failure_is_an_error(self, request):
        with self.assertRaisesRegex(RuntimeError, "permission denied"):
            hooks.ensure_project(BASE, "token", "demo", approved=True)

    @patch("ado_project.time.sleep")
    @patch("ado_project.request_json")
    def test_timeout_is_not_reported_as_success(self, request, sleep):
        request.side_effect = [None, {"id": OPERATION_ID}] + [{"status": "inProgress"}] * 90
        with self.assertRaises(TimeoutError):
            hooks.ensure_project(BASE, "token", "demo", approved=True)

    @patch("ado_project.build_opener")
    def test_only_404_is_treated_as_missing(self, opener):
        for code in (401, 403, 404, 429, 500):
            opener.return_value.open.side_effect = HTTPError(BASE, code, "failure", {}, io.BytesIO())
            with self.subTest(code=code):
                if code == 404:
                    self.assertIsNone(hooks.request_json(BASE, "token", "projects/demo", allow_missing=True))
                else:
                    with self.assertRaisesRegex(RuntimeError, f"HTTP {code}"):
                        hooks.request_json(BASE, "token", "projects/demo", allow_missing=True)

    @patch("ado_project.build_opener")
    def test_unmaterialized_identity_requires_browser_signin_not_project_creation_permission(self, opener):
        opener.return_value.open.side_effect = HTTPError(
            BASE, 403, "Forbidden",
            {"X-TFS-ServiceError": "Identity%20example%20has%20not%20been%20materialized%2C%20please%20use%20interactive%20login%20over%20the%20browser%20first."},
            io.BytesIO(),
        )
        with self.assertRaisesRegex(RuntimeError, "sign in with the same account") as raised:
            hooks.request_json(BASE, "token", "projects?$top=1&api-version=7.1")
        self.assertIn(BASE, str(raised.exception))
        self.assertNotIn("Create new projects permission", str(raised.exception))

    @patch("ado_project.build_opener")
    def test_read_forbidden_does_not_claim_project_creation_permission_is_required(self, opener):
        opener.return_value.open.side_effect = HTTPError(BASE, 403, "Forbidden", {}, io.BytesIO())
        with self.assertRaisesRegex(RuntimeError, "membership for the Azure CLI account") as raised:
            hooks.request_json(BASE, "token", "projects?$top=1&api-version=7.1")
        self.assertNotIn("Create new projects permission", str(raised.exception))

    @patch("ado_project.build_opener")
    def test_write_forbidden_includes_project_creation_permission(self, opener):
        opener.return_value.open.side_effect = HTTPError(BASE, 403, "Forbidden", {}, io.BytesIO())
        with self.assertRaisesRegex(RuntimeError, "Create new projects permission"):
            hooks.request_json(BASE, "token", "projects?api-version=7.1", {"name": "demo"})

    @patch("ado_project.build_opener")
    def test_html_signin_is_not_treated_as_a_project(self, opener):
        response = opener.return_value.open.return_value.__enter__.return_value
        response.headers = {"Content-Type": "text/html"}
        with self.assertRaisesRegex(RuntimeError, "did not return JSON"):
            hooks.request_json(BASE, "token", "projects/demo")

    @patch("ado_project.az_json")
    @patch("ado_project.request_json", side_effect=RuntimeError("organization unavailable"))
    @patch.dict("os.environ", {"AZURE_SUBSCRIPTION_ID": "subscription", "AZURE_LOCATION": "swedencentral"})
    def test_preflight_checks_organization_before_azure(self, request, azure):
        with self.assertRaisesRegex(RuntimeError, "organization unavailable"):
            hooks.preflight(BASE, "token")
        azure.assert_not_called()

    def test_redirects_do_not_forward_credentials(self):
        with self.assertRaisesRegex(RuntimeError, "redirect"):
            hooks.NoRedirect().redirect_request(None, None, 302, "", {}, "https://other.example")


if __name__ == "__main__":
    unittest.main()
