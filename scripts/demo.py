"""Replay all four workflows in a new output folder without changing the sample."""
from pathlib import Path
import os
import shutil
import subprocess
import uuid

ROOT = Path(__file__).resolve().parents[1]


def main():
    env = os.environ.copy()
    env["DOTNET_CLI_UI_LANGUAGE"] = "en"
    subprocess.run(["dotnet", "build", "Astra.App", "--configuration", "Release", "--nologo", "--verbosity", "quiet"], cwd=ROOT, env=env, check=True)
    output = ROOT / "output" / ("demo-" + uuid.uuid4().hex[:12])
    output.mkdir(parents=True)
    state = output / "state.json"
    shutil.copy2(ROOT / "samples" / "agency.json", state)
    app = ROOT / "Astra.App" / "bin" / "Release" / "net10.0" / "Astra.App.dll"
    transcript = []

    def run(arguments, expected=0):
        result = subprocess.run(["dotnet", str(app), "agency", *arguments], cwd=ROOT, env=env,
                                capture_output=True, text=True)
        message = "$ agency " + " ".join(arguments) + "\n" + result.stdout + result.stderr
        transcript.append(message)
        print(message, flush=True)
        if result.returncode != expected:
            raise RuntimeError(f"Expected exit {expected}, got {result.returncode}")
        return result.stdout

    run(["quote", str(state), "MOON-1", "4", "premium", "group10", "2030-06-01"])
    run(["reserve", str(state), "NEW", "MOON-1", "4", "premium", "group10", "2030-06-01"])
    before = state.read_bytes()
    run(["transfer", str(state), "NEW", "MOON-FULL", "premium", "group10", "2030-06-02"], expected=1)
    if state.read_bytes() != before:
        raise RuntimeError("A failed transfer changed the state file.")
    run(["find", str(state), "NEW"])
    run(["transfer", str(state), "NEW", "MOON-2", "premium", "group10", "2030-06-02"])
    run(["cancel", str(state), "NEW", "2030-06-08"])
    run(["reports", str(state), "2030-06-08", str(output / "reports.csv")])
    (output / "transcript.txt").write_text("\n".join(transcript), encoding="utf-8")
    print(f"Demo artifacts: {output}")


if __name__ == "__main__":
    main()
