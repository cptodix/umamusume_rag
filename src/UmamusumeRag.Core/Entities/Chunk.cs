namespace UmamusumeRag.Core.Entities;

// ===== RAG step 4/8 · STORE (the retrieval unit) ===========================
// A chunk is what gets embedded, what gets retrieved, and what gets pasted
// into the prompt. Its Content and Embedding must always describe the same
// text — never edit Content without re-embedding.
//
// Embedding is float[] here (database-agnostic); Infrastructure maps it to a
// pgvector `vector` column. See ChunkConfiguration.
// ==========================================================================

/// <summary>
/// A chunk of a <see cref="Document"/> together with its embedding vector.
/// </summary>
public class Chunk
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DocumentId { get; set; }

    public Document? Document { get; set; }

    /// <summary>Zero-based position of this chunk within its document (also used for citations).</summary>
    public int ChunkIndex { get; set; }

    /// <summary>The text that was embedded and that is shown to the model.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Embedding of <see cref="Content"/>. Stored as a pgvector `vector` column
    /// (mapping happens in the Infrastructure layer).
    ///
    /// GOTCHA: vectors are only comparable within the same embedding model.
    /// Changing Llm:EmbeddingModel makes every stored vector useless.
    /// </summary>
    public float[] Embedding { get; set; } = [];
}
