using System.Globalization;
using Astra.App;
using Xunit;

namespace Astra.Tests;

public sealed class AgencyCultureTests
{
    private static string Sample => Path.Combine(AppContext.BaseDirectory, "samples", "agency.json");

    [Fact]
    public void FlightDatesRemainGregorianIsoUnderArabicSaudiCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var before = File.ReadAllBytes(Sample);
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var english = Run("flights", Sample, "2030-06-01");
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            var arabic = Run("flights", Sample, "2030-06-01");

            Assert.Equal(0, english.Code);
            Assert.Equal(0, arabic.Code);
            Assert.Empty(english.Error);
            Assert.Empty(arabic.Error);
            Assert.Contains("2030-06-15", english.Output);
            Assert.Equal(english.Output, arabic.Output);
            Assert.Equal(before, File.ReadAllBytes(Sample));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void ReportConsoleAndCsvAreIdenticalAcrossEnglishAndArabicCalendars()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var directory = Path.Combine(Path.GetTempPath(), "astra-culture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "state.json");
        var export = Path.Combine(directory, "report.csv");
        File.Copy(Sample, path);
        var before = File.ReadAllBytes(path);
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var english = Run("reports", path, "2030-06-01", export);
            Assert.Equal(0, english.Code);
            Assert.Empty(english.Error);
            var englishCsv = File.ReadAllBytes(export);

            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            var arabic = Run("reports", path, "2030-06-01", export);
            Assert.Equal(0, arabic.Code);
            Assert.Empty(arabic.Error);
            Assert.Equal(english.Output, arabic.Output);
            Assert.Equal(englishCsv, File.ReadAllBytes(export));
            Assert.Contains(",2030-06-15,", File.ReadAllText(export));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            Directory.Delete(directory, recursive: true);
        }
    }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = CommandLine.Run(new[] { "agency" }.Concat(args).ToArray(), output, error);
        return (code, output.ToString(), error.ToString());
    }
}
