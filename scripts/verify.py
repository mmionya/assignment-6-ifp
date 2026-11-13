"""Run Release tests and record measured TRX evidence for PDF generation.

python scripts/verify.py [--git /path/to/git]
"""
import argparse
from datetime import datetime, timezone
import json
import os
import platform
import subprocess
import uuid
import xml.etree.ElementTree as ET

from release_evidence import ROOT, source_fingerprint


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--git", default="git")
    args = parser.parse_args()
    output = ROOT / "output"
    results = output / "test-results" / uuid.uuid4().hex
    results.mkdir(parents=True)
    fingerprint = source_fingerprint()
    env = os.environ.copy()
    env["DOTNET_CLI_UI_LANGUAGE"] = "en"
    command = ["dotnet", "test", "Astra.slnx", "--configuration", "Release", "--nologo",
               "--verbosity", "minimal", "--logger", "trx;LogFileName=tests.trx",
               "--results-directory", str(results)]
    result = subprocess.run(command, cwd=ROOT, env=env)
    tests = []
    counters = {}
    trx = results / "tests.trx"
    if trx.exists():
        tree = ET.parse(trx)
        ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
        counter_node = tree.find(".//t:Counters", ns)
        if counter_node is not None:
            counters = {name: int(value) for name, value in counter_node.attrib.items()}
        tests = [{"name": node.attrib["testName"], "outcome": node.attrib["outcome"]}
                 for node in tree.findall(".//t:UnitTestResult", ns)]
    evidence = {
        "source_commit": subprocess.check_output([args.git, "rev-parse", "HEAD"], cwd=ROOT).decode().strip(),
        "source_fingerprint": fingerprint,
        "finished_at_utc": datetime.now(timezone.utc).isoformat(),
        "sdk_version": subprocess.check_output(["dotnet", "--version"], cwd=ROOT).decode().strip(),
        "platform": f"{platform.system()} {platform.machine()}",
        "configuration": "Release",
        "command": command,
        "exit_code": result.returncode,
        "total": counters.get("total", 0), "passed": counters.get("passed", 0),
        "failed": counters.get("failed", 0), "skipped": counters.get("notExecuted", 0),
        "trx": trx.relative_to(ROOT).as_posix(),
        "tests": tests,
    }
    if fingerprint != source_fingerprint():
        evidence["exit_code"] = 1
        evidence["error"] = "Source files changed while tests were running; verify again."
    destination = output / "verification.json"
    destination.write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    print(f"Evidence: {destination}")
    print(f"Passed {evidence['passed']}/{evidence['total']}; failed {evidence['failed']}; skipped {evidence['skipped']}")
    return evidence["exit_code"] or (0 if tests and evidence["passed"] == evidence["total"] else 1)


if __name__ == "__main__":
    raise SystemExit(main())
