using Astra.Core;

namespace Astra.App;

// File access belongs at the application boundary, outside the pure core.
public static class BookingFiles
{
    public static Result<string> Read(string path)
    {
        try
        {
            return Result<string>.Success(File.ReadAllText(path));
        }
        catch (FileNotFoundException)
        {
            return Result<string>.Failure($"Cannot read input '{path}': file not found.");
        }
        catch (DirectoryNotFoundException)
        {
            return Result<string>.Failure($"Cannot read input '{path}': directory not found.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return Result<string>.Failure($"Cannot read input '{path}': {ex.Message}");
        }
    }

    // Add file context to parsing/domain failures without changing their meaning.
    public static Result<T> Load<T>(string path, Func<string, Result<T>> process) =>
        Read(path).Bind(json => process(json).Match(
            Result<T>.Success,
            error => Result<T>.Failure($"Input '{path}': {error}")));

    public static Result<QuoteSummary> LoadQuotes(string path, DateOnly asOf,
        Func<BookingRequest, decimal> policy) =>
        Load(path, json => Input.Process(json, asOf, policy));

    public static Result<BookingRequest> FindBooking(string path, string? id) =>
        Load(path, json => Input.FindBooking(json, id));
}
