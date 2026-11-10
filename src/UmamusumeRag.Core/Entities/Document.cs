namespace UmamusumeRag.Core.Entities;

/// <summary>
/// A source document that has been ingested into the RAG pipeline.
/// </summary>
public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    /// <summary>Full path of the source file; identifies the document across re-ingests.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>SHA-256 of the extracted text, used to skip re-embedding unchanged files.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Chunk> Chunks { get; set; } = [];
}
