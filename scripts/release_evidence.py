"""Shared source fingerprint and evidence checks for release reports."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def source_fingerprint():
    """Hash build inputs, native binaries and tests; ignore generated build output."""
    paths = []
    for directory in ("Astra.Core", "Astra.App", "Astra.Tests"):
        for path in (ROOT / directory).rglob("*"):
            if path.is_file() and not any(part in {"bin", "obj", "TestResults"} for part in path.relative_to(ROOT).parts):
                paths.append(path)
    paths.extend(ROOT / name for name in ("Astra.slnx", "Directory.Build.props", "global.json", "NuGet.Config"))
    paths.extend(path for path in (ROOT / "samples").rglob("*") if path.is_file())
    digest = hashlib.sha256()
    for path in sorted(paths, key=lambda item: item.relative_to(ROOT).as_posix()):
        digest.update(path.relative_to(ROOT).as_posix().encode("utf-8") + b"\0")
        content = path.read_bytes()
        if b"\0" not in content:
            content = content.replace(b"\r\n", b"\n")
        digest.update(hashlib.sha256(content).digest())
    return digest.hexdigest()


def load_verification(path):
    evidence = json.loads(Path(path).read_text(encoding="utf-8"))
    if evidence.get("source_fingerprint") != source_fingerprint():
        raise ValueError("Build inputs changed after verification. Run scripts/verify.py again.")
    if evidence.get("exit_code") != 0 or evidence.get("failed") != 0 or evidence.get("passed", 0) != evidence.get("total"):
        raise ValueError("Reports require a complete passing test run with no skipped cases.")
    if not evidence.get("tests") or len(evidence["tests"]) != evidence["total"]:
        raise ValueError("Verification must contain an outcome for every test case.")
    return evidence


def test_result(evidence, method):
    matches = [test for test in evidence["tests"]
               if test["name"].split("(", 1)[0].endswith("." + method)]
    if not matches or any(test["outcome"] != "Passed" for test in matches):
        raise ValueError(f"No passing evidence for report test: {method}")
    return f"Passed ({len(matches)} case{'s' if len(matches) != 1 else ''})"
