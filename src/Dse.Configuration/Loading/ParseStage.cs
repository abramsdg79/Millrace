using System.Globalization;
using System.Text.Json;

namespace Dse.Configuration.Loading;

internal static class ParseStage
{
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The parsed document, which the caller disposes; null after reporting DSE100.</summary>
    public static JsonDocument? Run(string json, LoadState state)
    {
        try
        {
            JsonDocument document = JsonDocument.Parse(json, Options);
            state.Root = document.RootElement;
            return document;
        }
        catch (JsonException ex)
        {
            long line = (ex.LineNumber ?? 0) + 1;
            long column = (ex.BytePositionInLine ?? 0) + 1;
            state.Error(
                ConfigDiagnostics.Syntax,
                "$",
                string.Create(CultureInfo.InvariantCulture, $"The file is not valid JSON at line {line}, column {column}: {FirstSentence(ex.Message)}"),
                "Correct the JSON at that position. Comments and trailing commas are allowed; everything else must be strict JSON.");
            return null;
        }
    }

    // System.Text.Json appends "LineNumber: n | BytePositionInLine: m." to its messages; that is already in ours.
    private static string FirstSentence(string message)
    {
        int cut = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        string text = (cut < 0 ? message : message[..cut]).TrimEnd();
        return text.EndsWith('.') ? text : text + ".";
    }
}
