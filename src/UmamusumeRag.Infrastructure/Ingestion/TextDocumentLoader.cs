using UmamusumeRag.Core.Abstractions;

namespace UmamusumeRag.Infrastructure.Ingestion;

// ===== RAG step 1/8 · LOAD (plain text) =====================================
// .txt and Markdown. Markdown is a great source format for RAG because its
// headings give you natural chunk boundaries — if you want structure-aware
// chunking later, start here (e.g. split on "\n## ").
//
// TWEAK: title extraction (first "# " heading vs file name), or strip Markdown
//        syntax (**bold**, links) before chunking if it hurts retrieval.
// ============================================================================

/// <summary>Loads plain-text formats: .txt and Markdown (.md, .markdown).</summary>
public class TextDocumentLoader : IDocumentLoader
{
    // TWEAK: add extensions here, or implement a new IDocumentLoader.
    private static readonly string[] Extensions = [".txt", ".md", ".markdown"];

    public bool CanLoad(string filePath) =>
        Extensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase);

    public async Task<LoadedDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);

        // Use the first Markdown heading as the title when there is one.
        // The title is what the model sees as "(from \"...\")" and what appears
        // in the API's sources, so it is worth getting right.
        var firstLine = content.Split('\n', 2)[0].Trim();
        var title = firstLine.StartsWith("# ", StringComparison.Ordinal)
            ? firstLine[2..].Trim()
            : Path.GetFileNameWithoutExtension(filePath);

        return new LoadedDocument(title, Path.GetFullPath(filePath), content);
    }
}
