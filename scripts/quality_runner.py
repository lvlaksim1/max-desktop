#!/usr/bin/env python3
"""Offline engineering quality gate (Repo Factory standard v1).

Operates only inside the project checkout. Never installs/updates/releases software.
Mock/sandbox checks are not evidence of live host acceptance.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
CONFIG = ROOT / "quality" / "standard.json"
EXECUTABLES = {"python", "dotnet", "node"}
KINDS = {"unit", "build", "mock", "sandbox", "static"}
TAIL_LIMIT = 10000


def git_info(root, *args):
    try:
        result = subprocess.run(["git", "-C", str(root), *args], capture_output=True,
                                text=True, encoding="utf-8", errors="replace", timeout=10)
        return result.stdout.strip() if result.returncode == 0 else None
    except (OSError, subprocess.TimeoutExpired):
        return None


def load(config, profile):
    data = json.loads(Path(config).read_text(encoding="utf-8"))
    if data.get("schema") != "engineering-quality-v1" or not isinstance(data.get("project"), str):
        raise ValueError("Quality schema or project is invalid")
    checks = data.get("quick")
    extended = data.get("full_additional")
    if not isinstance(checks, list) or not isinstance(extended, list):
        raise ValueError("Check lists are required")
    if profile == "full":
        checks = checks + extended
    seen = set()
    for step in checks:
        if not isinstance(step, dict):
            raise ValueError("Invalid check")
        name, cmd, kind, timeout = (
            step.get("id"), step.get("command"), step.get("kind"), step.get("timeout_sec"))
        if not isinstance(name, str) or not name or name in seen:
            raise ValueError("Check id missing/duplicate")
        seen.add(name)
        if (kind not in KINDS or not isinstance(cmd, list) or not cmd
                or any(not isinstance(x, str) or not x for x in cmd)
                or cmd[0].lower() not in EXECUTABLES
                or type(timeout) is not int or not 1 <= timeout <= 600):
            raise ValueError("Invalid command/kind/timeout: " + name)
    return data, checks


def _tail(value):
    if isinstance(value, bytes):
        value = value.decode("utf-8", "replace")
    return (value or "")[-TAIL_LIMIT:]


def run_checks(checks, root):
    results = []
    halt = False
    for step in checks:
        item = {"id": step["id"], "kind": step["kind"],
                "command": step["command"], "status": "NOT_RUN",
                "exit_code": None, "seconds": 0, "stdout_tail": "", "stderr_tail": ""}
        if halt:
            item["reason"] = "Earlier check failed"
            results.append(item)
            continue
        start = time.monotonic()
        try:
            proc = subprocess.run(step["command"], cwd=root, text=True, encoding="utf-8",
                                  errors="replace", capture_output=True,
                                  timeout=step["timeout_sec"],
                                  env={**os.environ, "PYTHONIOENCODING": "utf-8"})
            item.update(status="PASS" if proc.returncode == 0 else "FAIL",
                        exit_code=proc.returncode, stdout_tail=_tail(proc.stdout),
                        stderr_tail=_tail(proc.stderr))
        except subprocess.TimeoutExpired as exc:
            item.update(status="TIMEOUT", stdout_tail=_tail(exc.stdout),
                        stderr_tail=_tail(exc.stderr))
        except OSError as exc:
            item.update(status="FAIL", stderr_tail=str(exc))
        item["seconds"] = round(time.monotonic() - start, 2)
        results.append(item)
        halt = item["status"] != "PASS"
    return results


def make_report(root, profile, data, checks):
    outcome = ("NOT_CONFIGURED" if not checks else
               "PASS" if all(step["status"] == "PASS" for step in checks) else "FAIL")
    return {
        "schema": "engineering-quality-report-v1",
        "time_utc": datetime.now(timezone.utc).isoformat(),
        "project": data["project"], "profile": profile, "workspace": str(root),
        "commit": git_info(root, "rev-parse", "HEAD"),
        "branch": git_info(root, "branch", "--show-current"),
        "source_dirty": bool(git_info(root, "status", "--porcelain")),
        "offline_gate": outcome,
        "live_e2e": "NOT_TESTED",
        "installed_app": "NOT_TESTED",
        "owner_acceptance": "NOT_TESTED",
        "release_authorized": False,
        "checks": checks,
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description="Local offline engineering verification")
    parser.add_argument("--profile", choices=["quick", "full"], default="quick")
    parser.add_argument("--plan", action="store_true")
    argv = parser.parse_args(argv)
    try:
        data, checks = load(CONFIG, argv.profile)
    except (OSError, ValueError, KeyError, json.JSONDecodeError) as exc:
        print("QUALITY_CONFIG_ERROR: " + str(exc), file=sys.stderr)
        return 2
    if argv.plan:
        print(json.dumps({"project": data["project"], "profile": argv.profile,
                          "configured_checks": checks,
                          "live_e2e": "NOT_TESTED"}, ensure_ascii=False, indent=2))
        return 0
    results = run_checks(checks, ROOT)
    report = make_report(ROOT, argv.profile, data, results)
    directory = ROOT / "quality" / "reports"
    directory.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    destination = directory / ("quality-" + argv.profile + "-" + stamp + ".json")
    destination.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    for item in results:
        print(item["status"].ljust(14) + item["id"])
    print("OFFLINE_GATE=" + report["offline_gate"])
    print("LIVE_E2E=NOT_TESTED INSTALLED_APP=NOT_TESTED OWNER_ACCEPTANCE=NOT_TESTED")
    print("REPORT=" + str(destination))
    return 0 if report["offline_gate"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
