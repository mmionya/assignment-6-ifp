# Verify, generate reports and package

The C# application needs .NET 10; Python is used only for release scripts and reports.
Run all commands from the repository root with `git` on PATH. Python 3.10+ is sufficient.

```powershell
python -m pip install -r scripts/requirements-reports.txt
python scripts/verify.py
$revision = git rev-parse HEAD
python scripts/make_reports.py --source-commit $revision
python scripts/package.py
```

The verification command runs the entire suite in Release, retains the TRX file and
writes `output/verification.json`. It records actual per-test outcomes, SDK/platform,
the tested source commit and a SHA-256 fingerprint of the C#, native, project and sample
inputs. A complete passing run is required. Source changes during the run invalidate it.

The PDF generator checks the fingerprint and extracts the outcome of every test cited
in the individual reports. It cannot substitute a manually supplied pass count. A
documentation-only commit does not require repeating unchanged application tests.

The editable individual report content is `docs/reports/beknur.json` and `miras.json`.
Both describe implementation scope, integration, functional techniques, exact tests,
technical problems and limitations. Review their factual contribution wording before
the individual defense. The group report is assembled by `scripts/make_reports.py`.

Final outputs:

- `output/pdf/ASTRA_Group_Project_Report.pdf`
- `output/pdf/Beknur_Individual_Report.pdf`
- `output/pdf/Miras_Individual_Report.pdf`

`report_metadata.json` records the source SHA, fingerprint and each PDF's SHA-256.
The packaging script checks those values, archives that exact source revision and adds
the two PDFs relevant to each student. The working tree must be clean. Published PDFs
may be committed after the source revision: this adds documents without changing the
verified code. `reports/` is the fallback when `output/pdf` has not been generated locally.

The default archives end in `_REVIEW.zip`. Once the students have completed their review:

```powershell
python scripts/package.py --reviewed
```

To use separately edited personal PDFs, provide both paths:

```powershell
python scripts/package.py --reviewed --miras-report C:/Reports/Miras.pdf --beknur-report C:/Reports/Beknur.pdf
```

The final names are `IT-2512_Zhumazhan_Miras_FinalExam.zip` and
`IT-2512_Abdykhamit_Beknur_FinalExam.zip`. Each archive contains matching source, native
materials, samples, README, the group report and that student's personal report.
The scripts do not upload files or grant instructor access.

If Git is not on PATH, each release script accepts `--git C:/path/to/git.exe`.
Python dependencies are unnecessary for running or testing the C# application itself.
