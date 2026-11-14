using Astra.App;
using Xunit;

namespace Astra.Tests;

public sealed class AgencyCommandInputTests
{
    private static string Sample => Path.Combine(AppContext.BaseDirectory, "samples", "agency.json");

    [Fact]
    public void RootHelpAndNoArgumentsShowAgencyUsage()
    {
        foreach (var args in new string[][] { [], ["help"], ["--help"], ["-h"] })
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            Assert.Equal(0, CommandLine.Run(args, output, error));
            Assert.Contains("ASTRA agency commands", output.ToString());
            Assert.Empty(error.ToString());
        }
    }

    [Fact]
    public void LegacyCommandsAreRejectedWithoutChangingState()
    {
        var before = File.ReadAllBytes(Sample);
        foreach (var args in new string[][]
        {
            ["demo"],
            ["report", Sample, "2030-06-01", "standard"],
            ["find", Sample, "DEMO-A"],
            [Sample, "both", "2030-06-01"]
        })
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            Assert.Equal(2, CommandLine.Run(args, output, error));
            Assert.Empty(output.ToString());
            Assert.NotEmpty(error.ToString());
            Assert.Equal(before, File.ReadAllBytes(Sample));
        }
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("flights")]
    [InlineData("cancel", "state.json", "DEMO-A")]
    [InlineData("find", "state.json", "DEMO-A", "unexpected")]
    public void UnknownCommandOrWrongArityReturnsUsageExitCode(params string[] args)
    {
        var result = Run(args);
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Output);
        Assert.Contains("ASTRA agency commands", result.Error);
    }

    [Theory]
    [InlineData("x", "explorer", "standard", "2030-06-01", "Passengers")]
    [InlineData("2147483648", "explorer", "standard", "2030-06-01", "Passengers")]
    [InlineData("2", "9", "standard", "2030-06-01", "Class")]
    [InlineData("2", "explorer", "unknown", "2030-06-01", "Policy")]
    [InlineData("2", "explorer", "standard", "bad-date", "Date")]
    public void InvalidSyntaxReturnsTwoBeforeOpeningStateOrCreatingLock(
        string count, string travelClass, string policy, string date, string expectedError)
    {
        var missing = Path.Combine(Path.GetTempPath(), "astra-syntax-" + Guid.NewGuid().ToString("N") + ".json");
        var result = Run("reserve", missing, "NEW", "MOON-1", count, travelClass, policy, date);
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Output);
        Assert.Contains(expectedError, result.Error);
        Assert.DoesNotContain("Cannot read", result.Error);
        Assert.False(File.Exists(missing));
        Assert.False(File.Exists(missing + ".lock"));
    }

    [Theory]
    [InlineData("flights")]
    [InlineData("reports")]
    public void ReadCommandsValidateDateSyntaxBeforeLoadingFiles(string command)
    {
        var result = Run(command, "missing-state.json", "2030-99-99");
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Output);
        Assert.Contains("Date must have format", result.Error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("7")]
    [InlineData("2147483647")]
    public void ParsedPassengerCountOutsideBusinessRangeRemainsDomainFailure(string count)
    {
        var before = File.ReadAllBytes(Sample);
        var result = Run("quote", Sample, "MOON-1", count, "explorer", "standard", "2030-06-01");
        Assert.Equal(1, result.Code);
        Assert.Empty(result.Output);
        Assert.Contains("Passengers", result.Error);
        Assert.Equal(before, File.ReadAllBytes(Sample));
    }

    [Fact]
    public void ValidSyntaxWithMissingStateRemainsFileFailure()
    {
        var missing = Path.Combine(Path.GetTempPath(), "astra-missing-" + Guid.NewGuid().ToString("N") + ".json");
        var result = Run("flights", missing, "2030-06-01");
        Assert.Equal(1, result.Code);
        Assert.Empty(result.Output);
        Assert.Contains("file not found", result.Error);
    }

    [Fact]
    public void ValidSyntaxWithMissingReservationRemainsDomainFailure()
    {
        var result = Run("find", Sample, "MISSING");
        Assert.Equal(1, result.Code);
        Assert.Empty(result.Output);
        Assert.Contains("was not found", result.Error);
    }

    [Fact]
    public void HelpAndValidQuoteRemainSuccessful()
    {
        var help = Run("help");
        Assert.Equal(0, help.Code);
        Assert.Contains("ASTRA agency commands", help.Output);
        Assert.Empty(help.Error);
        var quote = Run("quote", Sample, "MOON-1", "4", "premium", "group10", "2030-06-01");
        Assert.Equal(0, quote.Code);
        Assert.Contains("238140.00", quote.Output);
        Assert.Empty(quote.Error);
    }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = CommandLine.Run(new[] { "agency" }.Concat(args).ToArray(), output, error);
        return (code, output.ToString(), error.ToString());
    }
}
