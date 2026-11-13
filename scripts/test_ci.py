#!/usr/bin/env python3
"""Exercise CI reuse and package guards without network or repository writes."""

import contextlib
import io
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import resolve_validation as validation
import test_package_smoke as smoke

COMMIT = "a" * 40
PARENT = "b" * 40
REPOSITORY = "hashcade/fee-save-editor"


def run_record():
    return dict(head_sha=PARENT, event="push", head_branch="main", path=".github/workflows/build.yml",
                head_repository={"full_name": REPOSITORY}, run_number=10, run_attempt=1, id=1,
                status="completed", conclusion="success", html_url="https://github.com/example/run")


class ValidationReuseTests(unittest.TestCase):
    def test_requires_exactly_one_parent_and_only_version_change(self):
        for parents in (COMMIT, f"{COMMIT} {PARENT} {PARENT}"):
            with patch.object(validation, "output", return_value=parents):
                self.assertIsNone(validation.version_only_parent())
        for files in ("", "README.md", "VERSION\ninternal/save/changed.go", "VERSION"):
            with self.subTest(files=files), patch.object(validation, "output", side_effect=[f"{COMMIT} {PARENT}", files]):
                self.assertEqual(validation.version_only_parent(), PARENT if files == "VERSION" else None)

    def test_only_success_of_the_exact_trusted_main_build_is_reused(self):
        self.assertEqual(self.reuse([run_record()]), "https://github.com/example/run")
        invalid = [dict(head_sha=COMMIT), dict(head_branch="feature"), dict(event="pull_request"),
                   dict(path=".github/workflows/release.yml"), dict(head_repository={"full_name": "other/fork"}),
                   dict(status="in_progress", conclusion=None), dict(conclusion="failure"), dict(conclusion="cancelled")]
        for changes in invalid:
            with self.subTest(changes=changes):
                record = run_record()
                record.update(changes)
                self.assertIsNone(self.reuse([record]))
        self.assertIsNone(self.reuse([]))

    def test_newer_failure_overrides_an_older_success(self):
        newer = run_record()
        newer.update(run_attempt=2, conclusion="failure")
        self.assertIsNone(self.reuse([run_record(), newer]))

    def test_waits_for_existing_run_instead_of_duplicating_it(self):
        pending = run_record()
        pending.update(status="in_progress", conclusion=None)
        with patch.object(validation, "build_runs", side_effect=[[pending], [run_record()]]), \
                patch.object(validation.time, "monotonic", return_value=0), patch.object(validation.time, "sleep") as sleep, \
                contextlib.redirect_stdout(io.StringIO()):
            self.assertIsNotNone(validation.reusable_build(REPOSITORY, PARENT, 5))
            sleep.assert_called_once_with(5)

    def test_api_permission_network_and_invalid_payload_fall_back(self):
        for error in (subprocess.CalledProcessError(1, "gh"), subprocess.TimeoutExpired("gh", 30),
                      ValueError("invalid JSON"), KeyError("missing field"), TypeError("malformed record")):
            with self.subTest(error=error), patch.dict(os.environ, GITHUB_REPOSITORY=REPOSITORY), \
                    patch.object(validation, "version_only_parent", return_value=PARENT), \
                    patch.object(validation, "reusable_build", side_effect=error), contextlib.redirect_stdout(io.StringIO()):
                self.assertIsNone(validation.resolve(0))

    def test_writes_explicit_result_for_the_publish_gate(self):
        with tempfile.TemporaryDirectory(prefix="fee-ci-guard-") as directory:
            output = Path(directory) / "output"
            summary = Path(directory) / "summary"
            with patch.dict(os.environ, GITHUB_OUTPUT=str(output), GITHUB_STEP_SUMMARY=str(summary)), \
                    patch.object(validation, "resolve", return_value=None), patch("sys.argv", ["resolve_validation.py"]), \
                    contextlib.redirect_stdout(io.StringIO()):
                validation.main()
            self.assertEqual(output.read_text(), "reuse=false\n")
            self.assertIn("required", summary.read_text())

    def reuse(self, runs):
        with patch.object(validation, "build_runs", return_value=runs):
            return validation.reusable_build(REPOSITORY, PARENT, 0)


class PackageSmokeTests(unittest.TestCase):
    def test_gui_crash_is_not_treated_as_success(self):
        with patch.object(smoke.subprocess, "Popen") as launch:
            launch.return_value.wait.return_value = 1
            launch.return_value.poll.return_value = 1
            with self.assertRaises(ValueError):
                smoke.check_gui_startup(Path("gui"))

    def test_healthy_test_gui_is_terminated_after_checking(self):
        with patch.object(smoke.subprocess, "Popen") as launch, contextlib.redirect_stdout(io.StringIO()):
            process = launch.return_value
            process.wait.side_effect = [subprocess.TimeoutExpired("gui", 3), 0]
            process.poll.return_value = None
            smoke.check_gui_startup(Path("gui"))
            process.terminate.assert_called_once()
            process.kill.assert_not_called()

    def test_only_our_test_process_is_killed_if_termination_times_out(self):
        with patch.object(smoke.subprocess, "Popen") as launch, contextlib.redirect_stdout(io.StringIO()):
            process = launch.return_value
            process.wait.side_effect = [subprocess.TimeoutExpired("gui", 3), subprocess.TimeoutExpired("gui", 10), 0]
            process.poll.return_value = None
            smoke.check_gui_startup(Path("gui"))
            process.kill.assert_called_once()


class WorkflowGuardTests(unittest.TestCase):
    def test_current_runs_are_not_cancelled_and_publish_requires_validation(self):
        root = Path(__file__).resolve().parents[1]
        build = (root / ".github/workflows/build.yml").read_text()
        release = (root / ".github/workflows/release.yml").read_text()
        platforms = (root / ".github/workflows/platforms.yml").read_text()
        self.assertIn("cancel-in-progress: false", build)
        self.assertIn("needs: [reuse, validation, package]", release)
        self.assertIn("needs.reuse.result == 'success'", release)
        self.assertIn("needs.validation.result == 'success'", release)
        self.assertIn("needs.validation.result == 'skipped'", release)
        self.assertNotIn("test_cli.py", platforms)
        self.assertIn("go test -race ./internal/app ./internal/save", platforms)
        self.assertIn("verify-tag: true", release)


if __name__ == "__main__":
    unittest.main()
