namespace UmamusumeRag.Core.Models;

/// <summary>
/// Tunable knobs for the RAG pipeline, bound from the "Rag" configuration section.
/// </summary>
public class RagOptions
{
    /// <summary>Target chunk length in characters.</summary>
    public int ChunkSize { get; set; } = 800;

    /// <summary>How many characters of the previous chunk to repeat at the start of the next one.</summary>
    public int ChunkOverlap { get; set; } = 120;

    /// <summary>How many chunks to retrieve for each question.</summary>
    public int TopK { get; set; } = 5;

    /// <summary>Folder scanned by POST /api/documents/ingest when no path is given (relative to the app content root).</summary>
    public string DataPath { get; set; } = "data";
}
