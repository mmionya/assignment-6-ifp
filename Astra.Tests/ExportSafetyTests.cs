using Astra.App;
using Xunit;

namespace Astra.Tests;

public class ExportSafetyTests
{
    private const string EmptyState = """{"Version":1,"Flights":[],"Reservations":[]}""";

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0path")]
    public void InvalidDestinationIsAnExplicitFailureWithoutPartialOutput(string destination)
    {
        using var directory = new TemporaryDirectory();
        var input = directory.Write("state.json", EmptyState);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = AgencyCommand.Run(["reports", input, "2030-06-01", destination], output, error);

        Assert.Equal(1, code);
        Assert.Empty(output.ToString());
        Assert.NotEmpty(error.ToString());
        Assert.Equal(EmptyState, File.ReadAllText(input));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void SameInputWithDifferentPathSpellingCannotBeOverwritten()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.Write("state.json", EmptyState);
        var destination = System.IO.Path.Combine(directory.Path, ".", "state.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = AgencyCommand.Run(["reports", input, "2030-06-01", destination], output, error);

        Assert.Equal(1, code);
        Assert.Empty(output.ToString());
        Assert.Contains("never overwritten", error.ToString());
        Assert.Equal(EmptyState, File.ReadAllText(input));
    }

    [Fact]
    public void CaseOnlyInputAliasUsesThePlatformPathComparison()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.Write("state.json", EmptyState);
        var destination = System.IO.Path.Combine(directory.Path, "STATE.JSON");
        var expectedSafe = !OperatingSystem.IsWindows();

        var validation = ReportFiles.ValidateDestination(destination, input);

        Assert.Equal(expectedSafe, validation.IsSuccess);
        if (!expectedSafe)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(1, AgencyCommand.Run(["reports", input, "2030-06-01", destination], output, error));
            Assert.Contains("never overwritten", error.ToString());
            Assert.Empty(output.ToString());
        }
        Assert.Equal(EmptyState, File.ReadAllText(input));
    }

    [Fact]
    public void FormerFixedTemporaryFilenameCanSafelyBeTheInputFile()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.Write("report.csv.tmp", EmptyState);
        var destination = System.IO.Path.Combine(directory.Path, "report.csv");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = AgencyCommand.Run(["reports", input, "2030-06-01", destination], output, error);

        Assert.Equal(0, code);
        Assert.Empty(error.ToString());
        Assert.Equal(EmptyState, File.ReadAllText(input));
        Assert.Contains("Report,Flight availability", File.ReadAllText(destination));
        Assert.Equal(2, Directory.GetFiles(directory.Path).Length);
    }

    [Fact]
    public void TemporaryFilesAreDistinctSiblingsAndRemovedAfterSuccessfulReplacement()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.Write("report.csv", "old");
        var paths = new List<string>();
        Stream Open(string path)
        {
            paths.Add(path);
            return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }

        Assert.True(ReportFiles.Export(destination, ["first"], Open).IsSuccess);
        Assert.True(ReportFiles.Export(destination, ["second"], Open).IsSuccess);

        Assert.Equal(2, paths.Distinct().Count());
        Assert.All(paths, path => Assert.Equal(directory.Path, System.IO.Path.GetDirectoryName(path)));
        Assert.All(paths, path => Assert.False(File.Exists(path)));
        Assert.Equal(["second"], File.ReadAllLines(destination));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void WriteFailurePreservesOldTargetAndUnrelatedTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.Write("report.csv", "original report");
        var unrelated = directory.Write("report.csv.tmp", "unrelated input");
        TrackingFileStream? tracked = null;

        var result = ReportFiles.Export(destination, FailingLines(), path => tracked = new TrackingFileStream(path));

        Assert.False(result.IsSuccess);
        Assert.Contains("simulated write failure", result.Error);
        Assert.True(tracked!.Disposed);
        Assert.Equal("original report", File.ReadAllText(destination));
        Assert.Equal("unrelated input", File.ReadAllText(unrelated));
        Assert.Equal(2, Directory.GetFiles(directory.Path).Length);
        using var exclusive = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.CanWrite);
    }

    [Fact]
    public void FailedOpenDoesNotDeleteAFileItDoesNotOwn()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.Write("report.csv", "old");
        string? encounteredPath = null;
        var result = ReportFiles.Export(destination, ["new"], path =>
        {
            encounteredPath = path;
            File.WriteAllText(path, "created by a competing writer");
            throw new IOException("exclusive creation failed");
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("old", File.ReadAllText(destination));
        Assert.Equal("created by a competing writer", File.ReadAllText(encounteredPath!));
    }

    [Fact]
    public void ExportWritesTheLinesAndReleasesTheStreamAfterSuccess()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.Write("report.csv", "old content");
        TrackingFileStream? tracked = null;

        var result = ReportFiles.Export(destination, ["one", "two"], path => tracked = new TrackingFileStream(path));

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(tracked!.Disposed);
        Assert.Equal(["one", "two"], File.ReadAllLines(destination));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void UnexpectedErrorsStillReleaseTheStreamAndRemoveTheTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var destination = System.IO.Path.Combine(directory.Path, "report.csv");
        TrackingFileStream? tracked = null;

        Assert.Throws<InvalidOperationException>(() => ReportFiles.Export(destination,
            FailingLines(new InvalidOperationException("bug")), path => tracked = new TrackingFileStream(path)));

        Assert.True(tracked!.Disposed);
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void ExportToAMissingDirectoryFailsWithThePathAndLeavesNothingBehind()
    {
        using var directory = new TemporaryDirectory();
        var destination = System.IO.Path.Combine(directory.Path, "missing", "report.csv");

        var result = ReportFiles.Export(destination, ["line"]);

        Assert.False(result.IsSuccess);
        Assert.Contains(destination, result.Error);
        Assert.Contains("Cannot export", result.Error);
        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    private static IEnumerable<string> FailingLines(Exception? failure = null)
    {
        yield return "first";
        throw failure ?? new IOException("simulated write failure");
    }

    private sealed class TrackingFileStream : FileStream
    {
        public bool Disposed { get; private set; }
        public TrackingFileStream(string path) : base(path, FileMode.CreateNew, FileAccess.Write, FileShare.None) { }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "astra-export-safety-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public string Write(string name, string text)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, text);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
