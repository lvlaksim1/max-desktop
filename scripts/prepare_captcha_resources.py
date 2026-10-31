#!/usr/bin/env python3
"""Prepare generated VK captcha resources for local MAX Desktop builds.

Default is read-only; --prepare explicitly restores pinned npm dependencies and
generates resources. Never reads account credentials or starts network probes.
"""
from pathlib import Path
import argparse
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / "tools" / "captcha"
RESOURCES = ROOT / "src" / "MaxDesktop.App" / "Resources"


def ready():
    js = RESOURCES / "captcha-sdk.js"
    css = RESOURCES / "captcha-sdk.css"
    return js.is_file() and js.stat().st_size > 100 and css.is_file()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--prepare", action="store_true",
                        help="Install pinned build dependencies and bundle the local SDK")
    args = parser.parse_args()
    if args.prepare:
        for command in [
            ["npm", "install", "--ignore-scripts", "--no-audit", "--no-fund",
             "--no-save", "--package-lock=false"],
            ["npm", "run", "build"],
            ["npm", "run", "test"],
        ]:
            completed = subprocess.run(command, cwd=BUILD, check=False)
            if completed.returncode != 0:
                print("CAPTCHA_PREPARE_FAILED", file=sys.stderr)
                return completed.returncode or 1
    if not ready():
        print("CAPTCHA_RESOURCES_NOT_PREPARED: "
              "python scripts/prepare_captcha_resources.py --prepare", file=sys.stderr)
        return 1
    print("CAPTCHA_RESOURCES_READY")
    return 0


if __name__ == "__main__":
    sys.exit(main())
