namespace UmamusumeRag.Core.Entities;

/// <summary>
/// A chunk of a <see cref="Document"/> together with its embedding vector.
/// </summary>
public class Chunk
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DocumentId { get; set; }

    public Document? Document { get; set; }

    /// <summary>Zero-based position of this chunk within its document.</summary>
    public int ChunkIndex { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Embedding of <see cref="Content"/>. Stored as a pgvector `vector` column
    /// (mapping happens in the Infrastructure layer).
    /// </summary>
    public float[] Embedding { get; set; } = [];
}
