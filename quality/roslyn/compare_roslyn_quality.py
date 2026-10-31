#!/usr/bin/env python3
"""Measure Roslyn quality deltas against a checked-in project-specific baseline.

Baseline is explicit; the tool never silently changes acceptable thresholds.
"""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]


def head() -> str:
    result = subprocess.run(["git", "-C", str(ROOT), "rev-parse", "HEAD"],
                            text=True, capture_output=True, timeout=10)
    return result.stdout.strip() if result.returncode == 0 else ""


def metrics(doc):
    return {key: int(doc.get(key, 0)) for key in
            ("files_scanned","types_count","methods_count",
             "over_complexity_count","duplicate_method_groups")}


def compare(current, baseline):
    current_counts = metrics(current)
    old = baseline.get("metrics", {})
    delta = {key: current_counts[key] - int(old.get(key, 0)) for key in current_counts}
    violations = []
    for key in ("over_complexity_count", "duplicate_method_groups"):
        if delta[key] > 0:
            violations.append(key + " increased by " + str(delta[key]))
    return delta, violations


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline-create", action="store_true")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    report = ROOT / "quality/reports/roslyn-architecture.json"
    baseline_path = ROOT / "quality/roslyn-baseline.json"
    if not report.is_file():
        print("ROSLYN_REPORT_MISSING", file=sys.stderr)
        return 2
    current = json.loads(report.read_text(encoding="utf-8-sig"))
    if current.get("schema") != "roslyn-syntax-architecture-v1":
        print("ROSLYN_REPORT_SCHEMA_MISMATCH", file=sys.stderr)
        return 2
    if args.baseline_create:
        if baseline_path.exists():
            print("BASELINE_ALREADY_EXISTS, refusing overwrite", file=sys.stderr)
            return 2
        baseline = {"schema":"engineering-roslyn-baseline-v1",
                    "created_utc": datetime.now(timezone.utc).isoformat(),
                    "reference_commit": head(), "metrics": metrics(current),
                    "source": "Roslyn syntax tree, no semantic binding or runtime evidence"}
        baseline_path.write_text(json.dumps(baseline,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        print("ROSLYN_BASELINE_CREATED", baseline_path)
        return 0
    if not baseline_path.is_file():
        print("ROSLYN_BASELINE_MISSING: not comparing with an imaginary reference")
        return 2
    baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
    if baseline.get("schema") != "engineering-roslyn-baseline-v1":
        print("ROSLYN_BASELINE_INVALID", file=sys.stderr)
        return 2
    delta, violations = compare(current, baseline)
    print("ROSLYN_METRICS_DELTA=" + json.dumps(delta))
    print("ROSLYN_REGRESSIONS=" + str(len(violations)))
    for item in violations:
        print("REGRESSION " + item)
    return 1 if args.check and violations else 0


if __name__ == "__main__":
    raise SystemExit(main())
