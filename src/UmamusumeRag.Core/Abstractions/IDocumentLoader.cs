namespace UmamusumeRag.Core.Abstractions;

/// <summary>Text extracted from a source file, ready for chunking.</summary>
public record LoadedDocument(string Title, string SourcePath, string Content);

/// <summary>
/// Extracts raw text from one family of file formats (.md, .txt, .docx, ...).
/// Add a new implementation to support another format; the ingestion pipeline
/// discovers all registered loaders automatically.
/// </summary>
public interface IDocumentLoader
{
    /// <summary>Returns true when this loader can extract text from the given file.</summary>
    bool CanLoad(string filePath);

    Task<LoadedDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default);
}
