import base64
import json
import os
from pathlib import Path
import subprocess
import time
import unittest
from urllib.error import HTTPError, URLError
from urllib.parse import urlparse
from urllib.request import Request, urlopen
import uuid


ROOT = Path(__file__).resolve().parents[1]
BASE_URL = os.environ.get("STOCKAI_TEST_BASE_URL", "http://127.0.0.1:18081").rstrip("/")


def read_local_env():
    env_file = ROOT / ".env"
    values = {}
    if not env_file.is_file():
        return values
    for line in env_file.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        values[key.strip()] = value.strip().strip('"').strip("'")
    return values


LOCAL_ENV = read_local_env()
AUTH_USERNAME = os.environ.get(
    "STOCKAI_TEST_USERNAME", LOCAL_ENV.get("APP_AUTH_USERNAME", "")
)
AUTH_PASSWORD = os.environ.get(
    "STOCKAI_TEST_PASSWORD", LOCAL_ENV.get("APP_AUTH_PASSWORD", "")
)
AUTH_HEADER = "Basic " + base64.b64encode(
    f"{AUTH_USERNAME}:{AUTH_PASSWORD}".encode()
).decode()


class ComposeDeploymentTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if urlparse(BASE_URL).hostname not in ("127.0.0.1", "localhost"):
            raise ValueError("Deployment smoke tests must target a local Compose stack")
        if not AUTH_USERNAME or not AUTH_PASSWORD:
            raise ValueError(
                "Set APP_AUTH_USERNAME and APP_AUTH_PASSWORD in .env "
                "or STOCKAI_TEST_USERNAME/STOCKAI_TEST_PASSWORD"
            )

    def request(self, path, body=None, method=None, authorized=True):
        headers = {"Content-Type": "application/json"}
        if authorized:
            headers["Authorization"] = AUTH_HEADER
        request = Request(
            BASE_URL + path,
            data=json.dumps(body).encode() if body is not None else None,
            headers=headers, method=method,
        )
        with urlopen(request, timeout=5) as response:
            return response.status, response.read()

    def test_frontend_and_api_require_authentication(self):
        for path in ("/", "/api/aiprompts"):
            with self.subTest(path=path):
                with self.assertRaises(HTTPError) as raised:
                    self.request(path, authorized=False)
                self.assertEqual(raised.exception.code, 401)
                self.assertIn(
                    "Basic", raised.exception.headers.get("WWW-Authenticate", "")
                )

    def test_frontend_and_history_route_load(self):
        for path in ("/", "/watchlist"):
            with self.subTest(path=path):
                status, body = self.request(path)
                self.assertEqual(status, 200)
                self.assertIn(b'id="app"', body)

    def test_health_checks_the_backend(self):
        status, body = self.request("/health", authorized=False)
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(body)["status"], "healthy")

    def test_backend_can_reach_python_with_its_configured_address(self):
        result = subprocess.run(
            ["docker", "compose", "exec", "-T", "backend", "sh", "-c",
             'curl -fsS "$PYTHON_DATA_SERVICE_URL/health"'],
            cwd=ROOT, check=True, capture_output=True, text=True, timeout=10,
        )
        self.assertEqual(json.loads(result.stdout)["status"], "ok")

    def test_sse_delivers_a_connection_event_without_buffering(self):
        request = Request(
            BASE_URL + "/api/watchlist/trading-plan/events",
            headers={"Authorization": AUTH_HEADER},
        )
        with urlopen(request, timeout=3) as response:
            self.assertIn("text/event-stream", response.headers["Content-Type"])
            self.assertIsNone(response.headers.get("Content-Encoding"))
            first_event = response.readline().decode()
            self.assertTrue(first_event.startswith("data: "), first_event)
            self.assertEqual(json.loads(first_event[6:])["type"], "connected")

    def test_z_data_survives_backend_recreation(self):
        name = "deployment-smoke-" + uuid.uuid4().hex
        status, body = self.request("/api/aiprompts", {
            "name": name, "systemPrompt": "Local smoke test", "temperature": 0.5,
            "isActive": False, "isDefault": False,
        })
        self.assertEqual(status, 201)
        prompt_id = json.loads(body)["id"]
        try:
            subprocess.run(
                ["docker", "compose", "up", "-d", "--no-deps", "--force-recreate",
                 "--wait", "--wait-timeout", "120", "backend"],
                cwd=ROOT, check=True, timeout=150,
            )
            deadline = time.monotonic() + 15
            while True:
                try:
                    status, body = self.request(f"/api/aiprompts/{prompt_id}")
                    break
                except (HTTPError, URLError):
                    if time.monotonic() >= deadline:
                        raise
                    time.sleep(0.5)
            self.assertEqual(status, 200)
            self.assertEqual(json.loads(body)["name"], name)
        finally:
            self.request(f"/api/aiprompts/{prompt_id}", method="DELETE")


if __name__ == "__main__":
    unittest.main(verbosity=2)
