"""Package verified source and PDFs. Personal review remains an explicit release step.

python scripts/package.py [--git /path/to/git] [--reviewed]
Optional completed personal PDFs: --miras-report path --beknur-report path
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile

from release_evidence import ROOT, source_fingerprint


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--git", default="git")
    parser.add_argument("--reviewed", action="store_true", help="The students have reviewed their personal reports.")
    parser.add_argument("--miras-report", type=Path)
    parser.add_argument("--beknur-report", type=Path)
    args = parser.parse_args()

    def git(*arguments):
        return subprocess.check_output([args.git, *arguments], cwd=ROOT)

    if git("status", "--porcelain").strip():
        parser.error("Commit modified or untracked project files before packaging.")
    if bool(args.miras_report) != bool(args.beknur_report):
        parser.error("Provide both personal report paths, or neither.")
    reports = ROOT / "output" / "pdf"
    if not (reports / "report_metadata.json").exists():
        reports = ROOT / "reports"
    metadata = json.loads((reports / "report_metadata.json").read_text(encoding="utf-8"))
    revision = metadata.get("source_commit", "")
    if not re.fullmatch("[0-9a-f]{40}", revision):
        parser.error("Report metadata must identify a full Git source SHA.")
    if subprocess.run([args.git, "merge-base", "--is-ancestor", revision, "HEAD"], cwd=ROOT).returncode:
        parser.error("The report source commit is not an ancestor of this checkout.")
    if metadata.get("source_fingerprint") != source_fingerprint():
        parser.error("Build inputs changed after report verification. Verify and regenerate reports.")

    payloads = {}
    for name, expected in metadata["files"].items():
        data = (reports / name).read_bytes()
        if hashlib.sha256(data).hexdigest() != expected or not data.startswith(b"%PDF-"):
            parser.error(f"Report changed or is not a PDF: {name}")
        payloads[name] = data
    people = [("Zhumazhan_Miras", "Miras", args.miras_report),
              ("Abdykhamit_Beknur", "Beknur", args.beknur_report)]
    group_name = "ASTRA_Group_Project_Report.pdf"
    for _, first, replacement in people:
        if replacement is not None:
            data = replacement.read_bytes()
            if not data.startswith(b"%PDF-"):
                parser.error(f"Personal report is not a PDF: {replacement}")
            payloads[first + "_Individual_Report.pdf"] = data
    status = "reviewed personal reports" if args.reviewed else "personal review pending"
    suffix = "" if args.reviewed else "_REVIEW"
    output_folder = ROOT / "output"
    output_folder.mkdir(exist_ok=True)
    with zipfile.ZipFile(io.BytesIO(git("archive", "--format=zip", revision))) as source:
        for full_name, first, _ in people:
            output = output_folder / f"IT-2512_{full_name}_FinalExam{suffix}.zip"
            with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
                for item in source.infolist():
                    if item.is_dir() or item.filename.startswith("reports/"):
                        continue
                    content = source.read(item)
                    if item.filename == "README.md":
                        content += f"\nPackaged source branch: main\nPackaged source commit: {revision}\n".encode()
                    archive.writestr(item.filename, content)
                archive.writestr("SOURCE_VERSION.txt", f"Repository: https://github.com/mmionya/assignment-6-ifp\nBranch: main\nSource commit: {revision}\nReport status: {status}\n")
                for name in (group_name, first + "_Individual_Report.pdf"):
                    archive.writestr("reports/" + name, payloads[name])
            print(output)


if __name__ == "__main__":
    main()
