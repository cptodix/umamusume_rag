using UmamusumeRag.Core.Abstractions;

namespace UmamusumeRag.Infrastructure.Ingestion;

/// <summary>Loads plain-text formats: .txt and Markdown (.md, .markdown).</summary>
public class TextDocumentLoader : IDocumentLoader
{
    private static readonly string[] Extensions = [".txt", ".md", ".markdown"];

    public bool CanLoad(string filePath) =>
        Extensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase);

    public async Task<LoadedDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);

        // Use the first Markdown heading as the title when there is one.
        var firstLine = content.Split('\n', 2)[0].Trim();
        var title = firstLine.StartsWith("# ", StringComparison.Ordinal)
            ? firstLine[2..].Trim()
            : Path.GetFileNameWithoutExtension(filePath);

        return new LoadedDocument(title, Path.GetFullPath(filePath), content);
    }
}
