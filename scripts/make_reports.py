"""Generate reviewable PDF reports after verification. Requires reportlab.

python scripts/make_reports.py --source-commit <git SHA> --verification output/verification.json
"""
import argparse
import json
import hashlib
import subprocess
from pathlib import Path
from xml.sax.saxutils import escape

from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, PageBreak


from release_evidence import load_verification, test_result

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "output" / "pdf"
NAVY = colors.HexColor("#142D4E")
TEAL = colors.HexColor("#087E8B")
PALE = colors.HexColor("#EFF5FA")
styles = getSampleStyleSheet()
styles.add(ParagraphStyle(name="TitleAstra", fontName="Helvetica-Bold", fontSize=27,
                          leading=31, textColor=NAVY, spaceAfter=14))
styles.add(ParagraphStyle(name="SubtitleAstra", fontSize=11, leading=16,
                          textColor=TEAL, spaceAfter=14))
styles.add(ParagraphStyle(name="BodyAstra", fontSize=10, leading=14, spaceAfter=8,
                          textColor=colors.HexColor("#243448")))
styles.add(ParagraphStyle(name="SmallAstra", fontSize=8.4, leading=11, spaceAfter=5))
styles.add(ParagraphStyle(name="HeadingAstra", fontName="Helvetica-Bold", fontSize=15,
                          leading=19, spaceBefore=10, spaceAfter=10, textColor=NAVY))
styles.add(ParagraphStyle(name="CodeAstra", fontName="Courier", fontSize=8.1,
                          leading=11, spaceAfter=7, wordWrap="CJK"))


def p(text, style="BodyAstra"):
    return Paragraph(text, styles[style])


def table(headers, rows, widths):
    data = [[p(escape(str(cell)), "SmallAstra") for cell in row]
            for row in [headers, *rows]]
    result = Table(data, colWidths=[width * mm for width in widths], repeatRows=1, hAlign="LEFT")
    result.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), PALE),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LINEBELOW", (0, 0), (-1, 0), 1, TEAL),
        ("LINEBELOW", (0, 1), (-1, -1), 0.35, colors.HexColor("#D8E2ED")),
        ("LEFTPADDING", (0, 0), (-1, -1), 7),
        ("RIGHTPADDING", (0, 0), (-1, -1), 7),
        ("TOPPADDING", (0, 0), (-1, -1), 7),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 7),
    ]))
    return result


def footer(canvas, doc):
    canvas.saveState()
    canvas.setStrokeColor(TEAL)
    canvas.line(20 * mm, 17 * mm, 190 * mm, 17 * mm)
    canvas.setFont("Helvetica", 8)
    canvas.setFillColor(NAVY)
    canvas.drawString(20 * mm, 12 * mm, "ASTRA | Introduction to Functional Programming | IT-2512")
    canvas.drawRightString(190 * mm, 12 * mm, str(doc.page))
    canvas.restoreState()


def write(filename, story):
    document = SimpleDocTemplate(str(OUT / filename), pagesize=A4,
        rightMargin=20 * mm, leftMargin=20 * mm, topMargin=20 * mm, bottomMargin=25 * mm,
        title=filename.removesuffix(".pdf").replace("_", " "), author="ASTRA team")
    document.build(story, onFirstPage=footer, onLaterPages=footer)


def group_report(revision, evidence):
    count = evidence["passed"]
    platform = escape(evidence["platform"])
    sdk = escape(evidence["sdk_version"])
    story = [p("ASTRA", "TitleAstra"), p("Space Travel Agency - Group Project Report", "SubtitleAstra"),
        p("<b>Team:</b> Zhumazhan Miras and Abdykhamit Beknur<br/><b>Group:</b> IT-2512<br/>"
          f"<b>Prepared:</b> 14 November 2026<br/><b>Platform verified:</b> {platform} / .NET SDK {sdk}"),
        p("Problem and implemented scope", "HeadingAstra"),
        p("A fictional space-travel agent needs consistent quotes, seat availability and reservation changes. "
          "ASTRA prevents overbooking and invalid changes while making every rule testable without files or console I/O. "
          "Its intended users are sales and operations staff. It is a console application with four business workflows "
          "and current-state reports. It calculates contract amounts; it does not perform real payments."),
        table(["Workflow", "Rules and meaningful result"], [
            ["Quote", "Validate request; require future open flight and enough seats; calculate the selected policy without saving."],
            ["Reserve", "Reject reused IDs; confirm a validated quote; save a new immutable state occupying seats."],
            ["Cancel", "Require active booking before departure; calculate refund, mark cancelled and release seats."],
            ["Transfer", "Require another available flight of the same destination; reprice and atomically replace the booking."],
        ], [30, 140]),
        p("Assigned ownership", "HeadingAstra"),
        p("<b>Beknur:</b> validated models, immutable state, pricing, quote/confirmation and reports; corresponding "
          "factory, capacity and pricing tests. <b>Miras:</b> cancellation/transfer, persistence, CLI and export safety; "
          "corresponding change, file and integration tests. Both review the integrated application and documentation."),
        p("Each team member is responsible for reviewing their assigned modules and explaining their integration. "
          "Individual reports describe each student's actual contribution.", "SmallAstra"),
        PageBreak(),
        p("Architecture and functional design", "TitleAstra"),
        p("CLI arguments -> JSON load -> DTO validation -> immutable state -> pure workflow -> save -> console result. "
          "Failed dependent stages return Result failures and skip subsequent work."),
        table(["Area", "Types / methods and evidence"], [
            ["Domain models", "Flight and Reservation: private constructors and getter-only values. ReservationState.Create validates references, unique IDs, dates and capacity."],
            ["Pure composition", "ReservationBooking.ValidateRequest -> CheckAvailability -> CalculateQuote. The existing BookingPipeline also has SelectBookable -> CalculateQuotes -> Summarize."],
            ["Errors / absence", "Result<T>.Map/Bind in Reserve, Transfer and ReservationStore.Update. FindFlight and FindReservation return Option.Some/None. Missing flight, full flight and invalid status are explicit failures."],
            ["Safe ordering", "QuoteState.Pending/Ready/Completed have distinct payloads and pattern-matched transitions. ReservationChanges requires an active booking and valid target before replacement."],
            ["Configured rules", "ReservationPricing.CreatePolicy captures immutable PricingConfig. Group10 and Family5 are partially applied delegates actually used by CLI workflows."],
            ["Extensions / queries", "Upcoming and WithContext are used in legacy workflows. AgencyReports.Build uses filtering, projection, GroupBy, Count, Sum and deterministic ThenBy."],
            ["Lazy processing", "BookingQuotes.PriceLazily uses yield return. BookingReports materialises an immutable array before grouping/reuse; tests observe deferred execution and repeated enumeration."],
        ], [37, 133]),
        p("The new source files are in Astra.Core/Reservations. Console, JSON and native boundary code live in "
          "Astra.App. xUnit assertions live in Astra.Tests. The original Assignment 5-8 workflows and tests remain available."),
        PageBreak(),
        p("Rules, persistence and integration", "TitleAstra"),
        p("Rules", "HeadingAstra"),
        p("Reservations contain 1-6 passengers. IDs are trimmed, case-sensitive and unique, including cancelled records. "
          "Only future open flights can be quoted or reserved. Price is base fare x passenger count x class multiplier; "
          "Explorer=1, Premium=1.35, First=1.8. Group10 discounts 4+ passengers by 10%; Family5 discounts 2+ by 5%. "
          "Money is decimal, rounded to cents AwayFromZero."),
        p("Cancellation at least seven days before departure refunds the stored contract in full; one to six days "
          "refunds 50%. No changes are allowed on departure day or before booking date. Transfer preserves passenger "
          "count and booking ID; new price minus old price is the balance due. Failed transfer preserves the old booking."),
        p("Data and resource ownership", "HeadingAstra"),
        p("samples/agency.json contains Version 1, Flights and Reservations. Separate nullable DTO fields distinguish "
          "missing values from valid zero/false. Unknown/duplicate properties, invalid enums and malformed content fail. "
          "Factories validate rows, then the state factory checks cross-record consistency. Paths are explicit CLI inputs."),
        p("ReservationStore.Update owns an exclusive state.lock handle across read, operation and save. The zero-byte "
          "lock file remains to avoid deletion races. ReportFiles owns a unique same-directory temporary file, disposes "
          "its writer/stream and replaces the destination only after a complete write. Errors clean the temporary file. "
          "Exports reject the input path, Windows case aliases and symbolic links/junctions."),
        p("Immutable snapshots require O(n) memory. Iterators retain source/delegate references until disposal; "
          "materialisation avoids repeated work when results are reused. There are no long-lived caches, event handlers "
          "or native allocations. External writers ignoring the lock and power-loss durability are outside the guarantee."),
        p("Native C", "HeadingAstra"),
        p("astra_native.c exports int astra_group_eligible(int passengers, int minimum). It returns 1 or 0, and -1 for "
          "invalid values. NativeGroupEligibility declares DllImport with Cdecl and converts loader/binding faults into "
          "Result failures. Reports use the actual returned count of group-eligible bookings. There is no silent fallback. "
          "Windows x64 DLL and Linux x64 SO, headers and build scripts are included. The final Windows call was executed."),
        PageBreak(),
        p("Verification and examples", "TitleAstra"),
        p(f"<b>Measured automated result: {count} passed, 0 failed, 0 skipped.</b> Verified in Release on {platform}, "
          f".NET SDK {sdk}. Results are read from the TRX output captured by scripts/verify.py. "
          "The source fingerprint must match before a report can be generated. Original baseline: 108 passing cases."),
        table(["Scenario", "Expected and verified result"], [
            ["Quote / reserve", "MOON-1, 4 Premium, Group10: 238140.00 USD. Confirmation reduces available seats by four."],
            ["Transfer", "MOON-2 has 52000 base fare: new total 252720.00; balance +14580.00. Old seats are released."],
            ["Cancel", "Cancel transferred booking on 2030-06-08: full 252720.00 refund. Repeated cancellation fails."],
            ["Rejected transfer", "MOON-FULL has no capacity: Result failure; original JSON bytes and booking are unchanged."],
            ["File failures", "Missing/malformed state reports a failure; dependent operation is not invoked. Lock conflict prevents an update."],
            ["Reports", "Original agency sample: retained contract value 11860000.00 USD; native count 1. Empty/no-match/tied sorting tested."],
            ["Native boundary", "Real calls confirm threshold values; fresh subprocess without DLL returns controlled DllNotFoundException failure."],
            ["Export regression", "Case-only input/output alias and empty output path are rejected. Owned temp files/streams are cleaned on failure."],
        ], [35, 135]),
        p("Defects addressed", "HeadingAstra"),
        p("The earlier CSV export compared paths case-sensitively and could overwrite input JSON on Windows with an "
          "uppercase output spelling. Empty export paths escaped as exceptions. Both now return clear failures. A fixed "
          "'.tmp' path was replaced by unique, exclusively created temporary files to prevent source/temp collisions."),
        p("Additional regressions cover invalid fractional policy prices before rounding, syntax errors returning exit 2 "
          "while business failures return 1, and Gregorian ISO dates under en-US and ar-SA regional settings."),
        p("All tests compare actual and expected results with assertions. The suite includes pure validation, complete "
          "workflows, file failures, ordering, failure propagation, configured policies, lazy execution, resource handling "
          "and native integration. See the named test files in docs/TEAM_PLAN.md."),
        PageBreak(),
        p("Setup, version and limitations", "TitleAstra"),
        p("Install a .NET 10 SDK. Windows x64 native binary is included. For a rebuild, install an x64 GCC C11 toolchain "
          "and put gcc on PATH. Run from the repository root. First restore needs nuget.org access."),
        p("dotnet build Astra.slnx<br/>dotnet test Astra.slnx<br/>"
          "Copy-Item samples/agency.json demo-state.json<br/>"
          "dotnet run --project Astra.App -- agency help<br/>"
          "dotnet run --project Astra.App -- agency reports demo-state.json 2030-06-01", "CodeAstra"),
        p("Native rebuild on Windows:<br/>powershell -ExecutionPolicy Bypass -File Astra.App/native/build-windows.ps1<br/>"
          "Linux x64:<br/>sh Astra.App/native/build-linux.sh", "CodeAstra"),
        p("Submitted source revision", "HeadingAstra"),
        p("Repository: https://github.com/mmionya/assignment-6-ifp<br/>Branch: main<br/>"
          f"Source commit: {escape(revision)}", "SmallAstra"),
        p("This identifies the source used for the report. A packaged README and SOURCE_VERSION.txt repeat that "
          "revision. Git publication and Moodle upload are separate actions; a local commit alone is not an LMS submission."),
        p("Limitations", "HeadingAstra"),
        p("No authentication, real payments, traveler identity, database, production service or historical event ledger. "
          "Explicit dates support deterministic demonstrations, not an authenticated transaction clock. Finance reports "
          "describe the current stored contracts; transfer does not preserve a historical audit trail. Windows x64 was "
          "verified. Linux build materials are included but the final suite was not executed there. Personal review "
          "and contribution statements must be completed by each student before submission."),
        p("References", "HeadingAstra"),
        p("Course Final Project Requirements.pdf and supplied Moodle final-exam text; the team's existing "
          "assignment-6-ifp repository at d90e9f65c491a232fd2a8826be4a43ec269cbef3; .NET standard libraries; "
          "xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.13.0 and xunit.runner.visualstudio 3.1.1. ReportLab is used only "
          "to generate these documents."),
    ]
    write("ASTRA_Group_Project_Report.pdf", story)


def individual(profile_path, revision, evidence):
    profile = json.loads(profile_path.read_text(encoding="utf-8"))
    story = []
    for number, page in enumerate(profile["pages"]):
        if number:
            story += [PageBreak(), p(escape(page["title"]), "TitleAstra")]
        else:
            story += [p(escape(profile["name"]), "TitleAstra"),
                p("Individual Contribution Report", "SubtitleAstra"),
                p(f"<b>Group:</b> IT-2512<br/><b>Project:</b> ASTRA Space Travel Agency<br/>"
                  f"<b>Git identity:</b> {escape(profile['login'])}<br/>"
                  "<b>Team:</b> Zhumazhan Miras; Abdykhamit Beknur"),
                p(escape(profile["role"])), p(escape(page["title"]), "HeadingAstra")]
        for block in page["blocks"]:
            if "heading" in block:
                story.append(p(escape(block["heading"]), "HeadingAstra"))
            for paragraph in block.get("paragraphs", []):
                story.append(p(escape(paragraph)))
            if "tests" in block:
                story.append(table(["Test method", "Expected result", "Actual result"],
                    [[test["method"], test["expected"], test_result(evidence, test["method"])]
                     for test in block["tests"]], [62, 81, 27]))
                story.append(Spacer(1, 4 * mm))
                story.append(p(f"Complete shared suite: {evidence['passed']} passed, 0 failed, 0 skipped; "
                    f"Release / {escape(evidence['platform'])} / .NET {escape(evidence['sdk_version'])}. "
                    "The results above are extracted from actual test outcomes.", "SmallAstra"))
    story += [Spacer(1, 4 * mm),
        p("This report describes the assigned module scope and verified project behavior. "
          "Each student should review this account before the individual defense.", "SmallAstra"),
        p(f"Repository: https://github.com/mmionya/assignment-6-ifp<br/>"
          f"Branch: main<br/>Source revision: {escape(revision)}", "SmallAstra")]
    write(profile["filename"], story)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--verification", default=str(ROOT / "output" / "verification.json"))
    parser.add_argument("--git", default="git")
    args = parser.parse_args()
    revision = subprocess.check_output([args.git, "rev-parse", "HEAD"], cwd=ROOT).decode().strip()
    if args.source_commit != revision:
        parser.error("source-commit must match the current checkout HEAD")
    try:
        evidence = load_verification(args.verification)
    except (ValueError, OSError, KeyError) as error:
        parser.error(str(error))
    OUT.mkdir(parents=True, exist_ok=True)
    group_report(revision, evidence)
    for person in ("beknur", "miras"):
        individual(ROOT / "docs" / "reports" / (person + ".json"), revision, evidence)
    filenames = ["ASTRA_Group_Project_Report.pdf", "Beknur_Individual_Report.pdf", "Miras_Individual_Report.pdf"]
    (OUT / "report_metadata.json").write_text(json.dumps({
        "source_commit": revision, "source_fingerprint": evidence["source_fingerprint"],
        "passed_tests": evidence["passed"], "individual_reports": "technical reports for personal review",
        "files": {name: hashlib.sha256((OUT / name).read_bytes()).hexdigest() for name in filenames}
    }, indent=2), encoding="utf-8")
    print("Generated three verified PDF reports in", OUT)


if __name__ == "__main__":
    main()
