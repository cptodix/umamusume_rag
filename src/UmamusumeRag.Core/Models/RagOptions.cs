namespace UmamusumeRag.Core.Models;

/// <summary>
/// Tunable knobs for the RAG pipeline, bound from the "Rag" configuration
/// section in src/UmamusumeRag.Api/appsettings.json.
///
/// This is the fastest place to experiment: change a value, restart the API,
/// re-ingest with ?force=true (only needed for chunking changes), and compare
/// answers on your baseline question set. See docs/02-next-steps.md.
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
}
