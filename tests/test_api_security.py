import base64
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
from threading import Thread
import time
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


ROOT = Path(__file__).resolve().parents[1]
API_DLL = Path(os.environ.get(
    "STOCKAI_TEST_API_DLL",
    ROOT / "src/StockAnalyse.Api/bin/Debug/net10.0/StockAnalyse.Api.dll",
)).resolve()
USERNAME = "stockai-security-test"
PASSWORD = "local-security-test-password"


def basic_auth(username=USERNAME, password=PASSWORD):
    token = base64.b64encode(f"{username}:{password}".encode()).decode()
    return {"Authorization": f"Basic {token}"}


class FakeOpenAIServer:
    def __init__(self):
        self.authorization_headers = []
        test_case = self

        class Handler(BaseHTTPRequestHandler):
            def do_POST(self):
                test_case.authorization_headers.append(
                    self.headers.get("Authorization", "")
                )
                length = int(self.headers.get("Content-Length", "0"))
                self.rfile.read(length)
                body = json.dumps({
                    "choices": [{"message": {"content": "ok"}}],
                }).encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def log_message(self, format, *args):
                pass

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = Thread(target=self.server.serve_forever, daemon=True)

    @property
    def endpoint(self):
        return f"http://127.0.0.1:{self.server.server_port}/v1/chat/completions"

    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=5)


class ApiSecurityTests(unittest.TestCase):
    def setUp(self):
        self.workspace = tempfile.TemporaryDirectory(prefix="stockai-security-")
        self.addCleanup(self.workspace.cleanup)
        self.root = Path(self.workspace.name)
        self.database = self.root / "data" / "stockanalyse.db"
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            self.port = listener.getsockname()[1]
        self.base_url = f"http://127.0.0.1:{self.port}"
        self.process = None
        self.log = (self.root / "api.log").open("w+")
        self.addCleanup(self.log.close)
        self.addCleanup(self.stop_api)

    def start_api(self):
        workdir = self.root / "work"
        workdir.mkdir()
        environment = os.environ | {
            "ASPNETCORE_URLS": self.base_url,
            "ASPNETCORE_ENVIRONMENT": "Production",
            "ConnectionStrings__DefaultConnection": f"Data Source={self.database}",
            "APP_AUTH_USERNAME": USERNAME,
            "APP_AUTH_PASSWORD": PASSWORD,
        }
        self.process = subprocess.Popen(
            ["dotnet", str(API_DLL)], cwd=workdir, env=environment,
            stdout=self.log, stderr=subprocess.STDOUT,
        )
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                self.log.seek(0)
                self.fail(f"API exited during startup:\n{self.log.read()}")
            try:
                status, _, _ = self.request("/health", authorized=False)
                if status == 200:
                    return
            except (URLError, TimeoutError):
                pass
            time.sleep(0.1)
        self.fail("API did not start within 30 seconds")

    def stop_api(self):
        if self.process is not None and self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=10)

    def request(self, path, body=None, authorized=True, headers=None, method=None):
        request_headers = {"Content-Type": "application/json"}
        if authorized:
            request_headers.update(basic_auth())
        if headers:
            request_headers.update(headers)
        request = Request(
            self.base_url + path,
            data=json.dumps(body).encode() if body is not None else None,
            headers=request_headers,
            method=method,
        )
        try:
            with urlopen(request, timeout=5) as response:
                return response.status, response.read(), response.headers
        except HTTPError as error:
            return error.code, error.read(), error.headers

    def create_config(self, api_key, endpoint="http://127.0.0.1:1/v1/chat/completions"):
        status, body, _ = self.request("/api/aimodelconfig", {
            "name": "security-test-model",
            "apiKey": api_key,
            "subscribeEndpoint": endpoint,
            "modelName": "test-model",
            "isActive": False,
            "isDefault": False,
        })
        self.assertEqual(status, 201, body)
        return json.loads(body)

    def test_api_rejects_missing_and_wrong_credentials(self):
        self.start_api()

        status, _, headers = self.request("/api/aiprompts", authorized=False)
        self.assertEqual(status, 401)
        self.assertIn("Basic", headers.get("WWW-Authenticate", ""))

        status, _, _ = self.request(
            "/api/aiprompts", headers=basic_auth(password="wrong-password")
        )
        self.assertEqual(status, 401)

        status, _, _ = self.request("/api/aiprompts")
        self.assertEqual(status, 200)

        status, body, _ = self.request("/health", authorized=False)
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(body)["status"], "healthy")

    def test_api_refuses_to_start_without_auth_configuration(self):
        workdir = self.root / "unconfigured"
        workdir.mkdir()
        environment = os.environ | {
            "ASPNETCORE_URLS": self.base_url,
            "ASPNETCORE_ENVIRONMENT": "Production",
            "ConnectionStrings__DefaultConnection": f"Data Source={self.database}",
        }
        environment.pop("APP_AUTH_USERNAME", None)
        environment.pop("APP_AUTH_PASSWORD", None)

        result = subprocess.run(
            ["dotnet", str(API_DLL)],
            cwd=workdir,
            env=environment,
            capture_output=True,
            text=True,
            timeout=20,
        )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("APP_AUTH_USERNAME", result.stdout + result.stderr)

    def test_model_config_responses_never_expose_the_api_key(self):
        self.start_api()
        secret = "sk-must-never-be-returned-123456789"

        config = self.create_config(secret)
        self.assertNotIn("apiKey", config)
        self.assertTrue(config["hasApiKey"])

        for path in ("/api/aimodelconfig", f"/api/aimodelconfig/{config['id']}"):
            with self.subTest(path=path):
                status, body, _ = self.request(path)
                self.assertEqual(status, 200, body)
                self.assertNotIn(secret.encode(), body)
                self.assertNotIn(b'"apiKey"', body)

    def test_cross_origin_writes_are_rejected(self):
        self.start_api()
        prompt = {
            "name": "same-origin-check",
            "systemPrompt": "security test",
            "temperature": 0.5,
            "isActive": False,
            "isDefault": False,
        }

        status, body, _ = self.request(
            "/api/aiprompts",
            prompt,
            headers={"Origin": "https://evil.example"},
        )
        self.assertEqual(status, 403, body)

        status, body, _ = self.request(
            "/api/aiprompts",
            prompt,
            headers={"Origin": self.base_url},
        )
        self.assertEqual(status, 201, body)
        prompt_id = json.loads(body)["id"]
        status, _, _ = self.request(
            f"/api/aiprompts/{prompt_id}", method="DELETE",
            headers={"Origin": self.base_url},
        )
        self.assertEqual(status, 204)

    def test_create_config_requires_an_api_key(self):
        self.start_api()
        status, body, _ = self.request("/api/aimodelconfig", {
            "name": "missing-key",
            "apiKey": "",
            "subscribeEndpoint": "http://127.0.0.1:1/v1/chat/completions",
            "modelName": "test-model",
        })
        self.assertEqual(status, 400, body)

    def test_empty_key_update_preserves_key_and_stored_test_uses_it(self):
        self.start_api()
        secret = "sk-saved-key-for-connection-test"

        with FakeOpenAIServer() as upstream:
            config = self.create_config(secret, upstream.endpoint)

            status, body, _ = self.request("/api/aimodelconfig/test", {
                "id": config["id"],
            })
            self.assertEqual(status, 200, body)

            status, body, _ = self.request(
                f"/api/aimodelconfig/{config['id']}",
                {
                    "name": "renamed-without-new-key",
                    "apiKey": "",
                    "subscribeEndpoint": upstream.endpoint,
                    "modelName": "test-model",
                    "isActive": False,
                    "isDefault": False,
                },
                method="PUT",
            )
            self.assertEqual(status, 200, body)
            self.assertNotIn(secret.encode(), body)
            self.assertNotIn(b'"apiKey"', body)

            status, body, _ = self.request("/api/aimodelconfig/test", {
                "id": config["id"],
            })
            self.assertEqual(status, 200, body)

        self.assertEqual(
            upstream.authorization_headers,
            [f"Bearer {secret}", f"Bearer {secret}"],
        )


if __name__ == "__main__":
    unittest.main(verbosity=2)
