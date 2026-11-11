namespace UmamusumeRag.Core.Models;

/// <summary>
/// Tunable knobs for the RAG pipeline, bound from the "Rag" configuration
/// section in src/UmamusumeRag.Api/appsettings.json.
///
/// This is the fastest place to experiment: change a value, restart the API,
/// re-ingest with ?force=true (only needed for chunking/embedding changes),
/// and compare answers on your baseline question set. See docs/02-next-steps.md.
/// </summary>
public class RagOptions
{
    /// <summary>
    /// Target chunk length in characters (RAG step 2).
    ///
    /// TWEAK: try 300 / 800 / 1500. Smaller = more precise retrieval, less
    ///        context per hit. Requires re-ingest (?force=true) because the
    ///        stored chunks change.
    /// </summary>
    public int ChunkSize { get; set; } = 800;

    /// <summary>
    /// How many characters of the previous chunk are repeated at the start of
    /// the next one (RAG step 2).
    ///
    /// WHY: without overlap, an answer that straddles a chunk boundary becomes
    ///      unfindable from either side. Must be smaller than ChunkSize.
    /// </summary>
    public int ChunkOverlap { get; set; } = 120;

    /// <summary>
    /// How many chunks are retrieved for each question (RAG step 6).
    ///
    /// TWEAK: try 1 / 3 / 5 / 10. Too small = the answer chunk may be missing;
    ///        too large = more noise, more prompt tokens, higher latency.
    ///        Can be overridden per request via AskRequest.TopK.
    /// </summary>
    public int TopK { get; set; } = 5;

    /// <summary>
    /// Folder scanned by POST /api/documents/ingest when no path is given.
    /// Relative paths resolve against the API project directory (content root),
    /// so the default "../../data" points at the repo's data/ folder.
    /// </summary>
    public string DataPath { get; set; } = "data";

    /// <summary>
    /// Text prepended to every chunk before embedding (RAG step 3).
    ///
    /// WHY: some embedding models are trained with asymmetric prefixes and lose
    ///      quality without them. Examples:
    ///        E5 family      -> "passage: "
    ///        nomic-embed    -> "search_document: "
    ///        BGE-M3, Qwen3  -> none (leave empty)
    ///      Must match <see cref="QueryPrefix"/>'s convention.
    /// </summary>
    public string DocumentPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Text prepended to the question before embedding (RAG step 5).
    ///
    /// WHY: the query half of the same asymmetric convention:
    ///        E5 family      -> "query: "
    ///        nomic-embed    -> "search_query: "
    ///        BGE-M3, Qwen3  -> none (leave empty)
    /// </summary>
    public string QueryPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Optional Matryoshka truncation: request a smaller vector (e.g. 512
    /// instead of the model's native 1024) to shrink storage and speed up
    /// search, at some recall cost. Null = the model's native dimension.
    ///
    /// GOTCHA: this changes the stored vector width. Re-ingest (?force=true)
    ///         after changing it, and lock the same dimension if you add HNSW.
    /// </summary>
    public int? EmbeddingDimensions { get; set; }
}
