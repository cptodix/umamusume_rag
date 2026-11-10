namespace UmamusumeRag.Core.Models;

/// <summary>Row returned by GET /api/documents.</summary>
public record DocumentSummary(Guid Id, string Title, string SourcePath, int ChunkCount, DateTimeOffset CreatedAt);

/// <summary>A chunk returned by vector search.</summary>
public record RetrievedChunk(
    Guid DocumentId,
    string DocumentTitle,
    string SourcePath,
    int ChunkIndex,
    string Content,
    double Score);
