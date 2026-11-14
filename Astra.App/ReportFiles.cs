using System.Text;
using Astra.Core;

namespace Astra.App;

public static class ReportFiles
{
    public static Result<string> ValidateDestination(string path, string? protectedInputPath = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Result<string>.Failure("Export path is missing.");
        try
        {
            var destination = Path.GetFullPath(path);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (protectedInputPath is not null &&
                string.Equals(destination, Path.GetFullPath(protectedInputPath), comparison))
                return Result<string>.Failure("Export path must differ from the input file; the input is never overwritten.");

            if (HasLinkedComponent(destination) ||
                (protectedInputPath is not null && HasLinkedComponent(Path.GetFullPath(protectedInputPath))))
                return Result<string>.Failure("Export paths and protected input paths must not contain symbolic links or junctions.");
            return Result<string>.Success(destination);
        }
        catch (Exception ex) when (IsFileFailure(ex))
        {
            return Result<string>.Failure($"Cannot export to '{path}': {ex.Message}");
        }
    }

    public static Result<string> Export(string path, IEnumerable<string> lines, Func<string, Stream>? openStream = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var destination = ValidateDestination(path);
        if (!destination.IsSuccess) return destination;

        var tempPath = Path.Combine(Path.GetDirectoryName(destination.Value)!,
            $".{Path.GetFileName(destination.Value)}.{Guid.NewGuid():N}.tmp");
        var ownsTemp = false;
        var open = openStream ?? (file => new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None));
        try
        {
            using (var stream = open(tempPath))
            {
                ownsTemp = true;
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                foreach (var line in lines)
                    writer.WriteLine(line);
            }
            File.Move(tempPath, destination.Value, overwrite: true);
            return Result<string>.Success(path);
        }
        catch (Exception ex) when (IsFileFailure(ex))
        {
            var note = ownsTemp ? RemoveTemp(tempPath) : "";
            return Result<string>.Failure($"Cannot export to '{path}': {ex.Message}{note}");
        }
        catch
        {
            if (ownsTemp) RemoveTemp(tempPath);
            throw;
        }
    }

    private static bool IsFileFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static bool HasLinkedComponent(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return false;
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
