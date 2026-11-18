import base64
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import time
import unittest
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


ROOT = Path(__file__).resolve().parents[1]
API_DLL = Path(os.environ.get(
    "STOCKAI_TEST_API_DLL",
    ROOT / "src/StockAnalyse.Api/bin/Debug/net10.0/StockAnalyse.Api.dll",
)).resolve()
USERNAME = "stockai-deployment-test"
PASSWORD = "local-deployment-test-password"


class ApiDeploymentTests(unittest.TestCase):
    def setUp(self):
        self.workspace = tempfile.TemporaryDirectory(prefix="stockai-api-")
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

    def start_api(self, directory):
        directory.mkdir()
        environment = os.environ | {
            "ASPNETCORE_URLS": self.base_url,
            "ASPNETCORE_ENVIRONMENT": "Production",
            "ConnectionStrings__DefaultConnection": f"Data Source={self.database}",
            "APP_AUTH_USERNAME": USERNAME,
            "APP_AUTH_PASSWORD": PASSWORD,
        }
        self.process = subprocess.Popen(
            ["dotnet", str(API_DLL)], cwd=directory, env=environment,
            stdout=self.log, stderr=subprocess.STDOUT,
        )
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                self.log.seek(0)
                self.fail(f"API exited during startup:\n{self.log.read()}")
            try:
                self.request("/api/aiprompts")
                return
            except (URLError, TimeoutError):
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

    def request(self, path, body=None):
        credentials = base64.b64encode(
            f"{USERNAME}:{PASSWORD}".encode()
        ).decode()
        request = Request(
            self.base_url + path,
            data=json.dumps(body).encode() if body is not None else None,
            headers={
                "Content-Type": "application/json",
                "Authorization": f"Basic {credentials}",
            },
        )
        try:
            with urlopen(request, timeout=3) as response:
                return response.status, response.read()
        except HTTPError as error:
            return error.code, error.read()

    def test_health_reports_database_readiness(self):
        self.start_api(self.root / "work")
        status, body = self.request("/health")
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(body)["status"], "healthy")

    def test_database_path_and_data_survive_a_new_working_directory(self):
        self.start_api(self.root / "first")
        status, body = self.request("/api/aiprompts", {
            "name": "deployment-persistence-check",
            "systemPrompt": "Local deployment test",
            "temperature": 0.5,
            "isActive": False,
            "isDefault": False,
        })
        self.assertEqual(status, 201, body)
        self.assertTrue(self.database.is_file(), "Configured database path was ignored")
        prompt_id = json.loads(body)["id"]
        self.stop_api()
        self.start_api(self.root / "second")
        status, body = self.request(f"/api/aiprompts/{prompt_id}")
        self.assertEqual(status, 200, body)
        self.assertEqual(json.loads(body)["name"], "deployment-persistence-check")
        self.assertFalse((self.root / "second" / "stockanalyse.db").exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
