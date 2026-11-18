from pathlib import Path
import os
import subprocess
import tarfile
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
SCRIPT_DIR = ROOT / "deploy" / "server"
SCRIPTS = sorted(SCRIPT_DIR.glob("*.sh"))


class ServerDeploymentScriptTests(unittest.TestCase):
    def test_shell_scripts_parse(self):
        self.assertTrue(SCRIPTS)
        for script in SCRIPTS:
            with self.subTest(script=script.name):
                subprocess.run(
                    ["bash", "-n", str(script)],
                    cwd=ROOT, check=True, capture_output=True, text=True,
                )

    def test_release_package_contains_scripts_but_no_secrets_or_build_artifacts(self):
        with tempfile.TemporaryDirectory(prefix="stockai-release-") as directory:
            archive = Path(directory) / "release.tar.gz"
            subprocess.run(
                ["bash", str(SCRIPT_DIR / "package-release.sh"), str(archive)],
                cwd=ROOT, check=True, capture_output=True, text=True,
            )
            with tarfile.open(archive, "r:gz") as package:
                names = {member.name.removeprefix("./") for member in package}

        for required in (
            "docker-compose.yml",
            "deploy/server/deploy-stockai.sh",
            "deploy/server/configure-nginx.sh",
            "deploy/server/verify-stockai.sh",
            "deploy/server/ci-deploy-wrapper.sh",
            "deploy/server/setup-cicd-user.sh",
            "deploy/server/setup-cicd-existing-user.sh",
            "python-data-service/config.example.ini",
            "src/StockAnalyse.Api/StockAnalyse.Api.csproj",
        ):
            self.assertIn(required, names)

        forbidden = {
            ".env",
            "config.ini",
            "NuGet.config",
            "python-data-service/config.ini",
        }
        self.assertFalse(forbidden & names)
        self.assertFalse(
            [name for name in names if "/bin/" in name or "/obj/" in name],
            "发布包不应包含 .NET 构建产物",
        )
        self.assertFalse(
            [name for name in names if "/node_modules/" in name],
            "发布包不应包含 node_modules",
        )

    def test_cicd_workflow_requires_a_production_environment_and_serialized_deploy(self):
        workflow = (ROOT / ".github" / "workflows" / "stockai.yml").read_text(
            encoding="utf-8"
        )

        self.assertIn("workflow_dispatch:", workflow)
        self.assertIn("name: production", workflow)
        self.assertIn("concurrency:", workflow)
        self.assertIn("cancel-in-progress: false", workflow)
        self.assertIn("sha256sum -c stockai-release.tar.gz.sha256", workflow)
        self.assertIn('"deploy $RELEASE_SHA $RELEASE_ID"', workflow)
        self.assertFalse(
            (ROOT / ".github" / "workflows" / "docker-deploy.yml").exists()
        )

    def test_forced_command_passes_original_command_as_single_argument(self):
        environment = os.environ.copy()
        environment["STOCKAI_SETUP_LIB_ONLY"] = "1"
        result = subprocess.run(
            [
                "bash",
                "-c",
                'source "$1"; authorized_keys_line /usr/bin/sudo '
                '"ssh-ed25519 AAAATEST"',
                "bash",
                str(SCRIPT_DIR / "setup-cicd-user.sh"),
            ],
            cwd=ROOT,
            check=True,
            capture_output=True,
            text=True,
            env=environment,
        )
        line = result.stdout.strip()
        self.assertTrue(line.startswith('restrict,command="'), line)
        self.assertIn('" ssh-ed25519 ', line)
        self.assertIn('\\"$SSH_ORIGINAL_COMMAND\\"', line)
        self.assertIn("/usr/bin/sudo -n /usr/local/sbin/stockai-ci-deploy", line)
        self.assertNotIn("env_keep", line)

        start = line.index('command="') + len('command="')
        end = line.index('" ', start)
        forced_command = line[start:end].replace('\\"', '"')
        self.assertTrue(forced_command.endswith('"$SSH_ORIGINAL_COMMAND"'))

        wrapper = (SCRIPT_DIR / "ci-deploy-wrapper.sh").read_text(encoding="utf-8")
        self.assertIn(
            'ORIGINAL_COMMAND="${1:-${SSH_ORIGINAL_COMMAND:-}}"',
            wrapper,
        )
        self.assertNotIn(
            'exec bash "$APP_DIR/deploy/server/deploy-stockai.sh"',
            wrapper,
        )
        self.assertIn(
            'DEPLOY_SCRIPT="$WORK_DIR/deploy/server/deploy-stockai.sh"',
            wrapper,
        )
        self.assertIn('bash "$DEPLOY_SCRIPT" "$ARCHIVE"', wrapper)

        with tempfile.TemporaryDirectory(prefix="stockai-forced-command-") as directory:
            temporary = Path(directory)
            fake_sudo = temporary / "sudo"
            fake_sudo.write_text(
                "#!/usr/bin/env bash\n"
                'if [[ "$1" == "-n" ]]; then shift; fi\n'
                'exec "$@"\n',
                encoding="utf-8",
            )
            fake_wrapper = temporary / "stockai-ci-deploy"
            fake_wrapper.write_text(
                "#!/usr/bin/env bash\n"
                'printf "%s" "$1" > "$CAPTURE"\n'
                'printf "%s" "$#" > "$CAPTURE.count"\n',
                encoding="utf-8",
            )
            fake_sudo.chmod(0o755)
            fake_wrapper.chmod(0o755)

            executable_command = (
                forced_command.replace("/usr/bin/sudo", str(fake_sudo))
                .replace(
                    "/usr/local/sbin/stockai-ci-deploy",
                    str(fake_wrapper),
                )
            )
            capture = temporary / "captured"
            simulation_env = {
                "PATH": os.environ.get("PATH", "/usr/bin:/bin"),
                "CAPTURE": str(capture),
            }

            deploy_command = f"deploy {'a' * 64} v1.0.0"
            subprocess.run(
                ["bash", "-c", executable_command],
                check=True,
                env={**simulation_env, "SSH_ORIGINAL_COMMAND": deploy_command},
            )
            self.assertEqual(capture.read_text(encoding="utf-8"), deploy_command)
            self.assertEqual(
                (temporary / "captured.count").read_text(encoding="utf-8"),
                "1",
            )

            injected_command = f"deploy {'a' * 64} v1; touch {temporary}/pwned"
            subprocess.run(
                ["bash", "-c", executable_command],
                check=True,
                env={**simulation_env, "SSH_ORIGINAL_COMMAND": injected_command},
            )
            self.assertFalse((temporary / "pwned").exists())
            self.assertEqual(
                capture.read_text(encoding="utf-8"),
                injected_command,
            )

    def test_existing_user_setup_restricts_a_dedicated_key_without_creating_an_account(self):
        script = (SCRIPT_DIR / "setup-cicd-existing-user.sh").read_text(
            encoding="utf-8"
        )
        self.assertIn('DEPLOY_USER="${DEPLOY_USER:-root}"', script)
        self.assertNotIn("useradd", script)
        self.assertNotIn("passwd -l", script)
        self.assertIn("该公钥已存在", script)
        self.assertNotIn("请先完成一次 StockAI 部署", script)

        environment = os.environ.copy()
        environment["STOCKAI_SETUP_LIB_ONLY"] = "1"

        def line_for(user, sudo_path):
            result = subprocess.run(
                [
                    "bash",
                    "-c",
                    'source "$1"; authorized_keys_line "$2" "$3" '
                    '"ssh-ed25519 AAAATEST"',
                    "bash",
                    str(SCRIPT_DIR / "setup-cicd-existing-user.sh"),
                    user,
                    sudo_path,
                ],
                cwd=ROOT,
                check=True,
                capture_output=True,
                text=True,
                env=environment,
            )
            return result.stdout.strip()

        root_line = line_for("root", "")
        self.assertEqual(
            root_line,
            'restrict,command="/usr/local/sbin/stockai-ci-deploy '
            '\\"$SSH_ORIGINAL_COMMAND\\"" ssh-ed25519 AAAATEST',
        )
        user_line = line_for("ubuntu", "/usr/bin/sudo")
        self.assertEqual(
            user_line,
            'restrict,command="/usr/bin/sudo -n '
            '/usr/local/sbin/stockai-ci-deploy '
            '\\"$SSH_ORIGINAL_COMMAND\\"" ssh-ed25519 AAAATEST',
        )

    def test_setup_scripts_accept_the_default_nuget_source(self):
        environment = os.environ.copy()
        environment["STOCKAI_SETUP_LIB_ONLY"] = "1"
        for script_name in (
            "setup-cicd-user.sh",
            "setup-cicd-existing-user.sh",
        ):
            for source, expected in (
                ("https://api.nuget.org/v3/index.json", True),
                ("https://mirrors.huaweicloud.com/repository/nuget/v3/index.json", True),
                ("http://api.nuget.org/v3/index.json", False),
            ):
                with self.subTest(script=script_name, source=source):
                    result = subprocess.run(
                        [
                            "bash",
                            "-c",
                            'source "$1"; [[ "$2" =~ $NUGET_PATTERN ]]',
                            "bash",
                            str(SCRIPT_DIR / script_name),
                            source,
                        ],
                        cwd=ROOT,
                        capture_output=True,
                        text=True,
                        env=environment,
                    )
                    self.assertEqual(result.returncode == 0, expected)


if __name__ == "__main__":
    unittest.main(verbosity=2)
