using System.Text;
using Astra.Core;

namespace Astra.App;

public static class ReportFiles
{
    public static Result<string> Export(string path, IEnumerable<string> lines, Func<string, Stream>? openStream = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (string.IsNullOrWhiteSpace(path))
            return Result<string>.Failure("Export path is missing.");

        var tempPath = path + ".tmp";
        var open = openStream ?? (file => new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None));
        try
        {
            using (var stream = open(tempPath))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                foreach (var line in lines)
                    writer.WriteLine(line);
            }
            File.Move(tempPath, path, overwrite: true);
            return Result<string>.Success(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            var note = RemoveTemp(tempPath);
            return Result<string>.Failure($"Cannot export to '{path}': {ex.Message}{note}");
        }
        catch
        {
            RemoveTemp(tempPath);
            throw;
        }
    }

    private static string RemoveTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $" The temporary file '{tempPath}' could not be removed: {ex.Message}";
        }
    }
}