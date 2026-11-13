"""Package the committed sources and reviewed PDF drafts; never uploads anything.

Usage: python scripts/package.py [--git /path/to/git] [--allow-draft-reports]
"""
import argparse
import io
import json
from pathlib import Path
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--git", default="git")
    parser.add_argument("--allow-draft-reports", action="store_true")
    args = parser.parse_args()

    def git(*arguments):
        return subprocess.check_output([args.git, *arguments], cwd=ROOT)

    if git("status", "--porcelain").strip():
        parser.error("Commit or remove untracked/modified source files before packaging.")
    revision = git("rev-parse", "HEAD").decode().strip()
    branch = git("branch", "--show-current").decode().strip()
    if not args.allow_draft_reports:
        parser.error("Individual reports are review drafts. Complete personal reports before submission, or use --allow-draft-reports for a clearly named review archive.")
    reports = ROOT / "output" / "pdf"
    group = reports / "ASTRA_Group_Project_Report.pdf"
    people = [("Zhumazhan_Miras", "Miras"), ("Abdykhamit_Beknur", "Beknur")]
    metadata = reports / "report_metadata.json"
    if not metadata.exists() or json.loads(metadata.read_text())["source_commit"] != revision:
        parser.error("Regenerate PDFs for the current source commit before packaging.")
    with zipfile.ZipFile(io.BytesIO(git("archive", "--format=zip", "HEAD"))) as source:
        for full_name, first in people:
            individual = reports / f"{first}_Individual_Review_Draft.pdf"
            if not group.is_file() or not individual.is_file():
                parser.error("Generate all PDF reports before packaging.")
            output = ROOT / "output" / f"IT-2512_{full_name}_FinalExam_REVIEW.zip"
            with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
                for item in source.infolist():
                    if item.is_dir():
                        continue
                    content = source.read(item)
                    if item.filename == "README.md":
                        content += f"\nPackaged source branch: {branch}\nPackaged source commit: {revision}\n".encode()
                    archive.writestr(item.filename, content)
                archive.writestr("SOURCE_VERSION.txt", f"Repository: https://github.com/mmionya/assignment-6-ifp\nBranch: {branch}\nSource commit: {revision}\nReport status: individual review drafts; complete before LMS submission.\n")
                archive.write(group, "reports/" + group.name)
                archive.write(individual, "reports/" + individual.name)
            print(output)


if __name__ == "__main__":
    main()
