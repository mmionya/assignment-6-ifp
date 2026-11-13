using Astra.App;
using Xunit;

namespace Astra.Tests;

public class ExportSafetyTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0path")]
    public void InvalidDestinationIsAnExplicitFailureWithoutPartialOutput(string destination)
    {
        using var directory = new TemporaryDirectory();
        var input = directory.Write("bookings.json", "[]");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = ReportCommand.Run([input, "2030-06-15", "standard", destination], output, error);

        Assert.Equal(1, code);
        Assert.Empty(output.ToString());
        Assert.NotEmpty(error.ToString());
        Assert.Equal("[]", File.ReadAllText(input));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void SameInputWithDifferentPathSpellingCannotBeOverwritten()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.Write("bookings.json", "[]");
        var destination = System.IO.Path.Combine(directory.Path, ".", "bookings.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = ReportCommand.Run([input, "2030-06-15", "standard", destination], output, error);

        Assert.Equal(1, code);
        Assert.Empty(output.ToString());
        Assert.Contains("never overwritten", error.ToString());
        Assert.Equal("[]", File.ReadAllText(input));
    }

    [Fact]
    public void CaseOnlyInputAliasUsesThePlatformPathComparison()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.Write("bookings.json", "[]");
        var destination = System.IO.Path.Combine(directory.Path, "BOOKINGS.JSON");
        var expectedSafe = !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS();

        var validation = ReportFiles.ValidateDestination(destination, input);

        Assert.Equal(expectedSafe, validation.IsSuccess);
        if (!expectedSafe)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(1, ReportCommand.Run([input, "2030-06-15", "standard", destination], output, error));
            Assert.Contains("never overwritten", error.ToString());
            Assert.Empty(output.ToString());
        }
        Assert.Equal("[]", File.ReadAllText(input));
    }

    [Fact]
    public void FormerFixedTemporaryFilenameCanSafelyBeTheInputFile()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.Write("report.csv.tmp", "[]");
        var destination = System.IO.Path.Combine(directory.Path, "report.csv");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = ReportCommand.Run([input, "2030-06-15", "standard", destination], output, error);

        Assert.Equal(0, code);
        Assert.Empty(error.ToString());
        Assert.Equal("[]", File.ReadAllText(input));
        Assert.Contains("Report,Quote list", File.ReadAllText(destination));
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

        var result = ReportFiles.Export(destination, FailingLines());

        Assert.False(result.IsSuccess);
        Assert.Contains("simulated write failure", result.Error);
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

    private static IEnumerable<string> FailingLines()
    {
        yield return "first";
        throw new IOException("simulated write failure");
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
