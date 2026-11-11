namespace UmamusumeRag.Core.Models;

// ===== RAG steps 5-8 · API contracts ======================================
// AskRequest  -> what the caller sends to POST /api/ask
// AskResponse -> step 8 output: the answer plus the chunks it was built from
// SourceReference -> one retrieved chunk as exposed to the caller
//
// `Score` is COSINE SIMILARITY (1.0 = identical, ~0 = unrelated), not distance.
// ==========================================================================

/// <summary>Request body for POST /api/ask.</summary>
public record AskRequest(string Question, int? TopK = null);

/// <summary>Answer generated from the retrieved context, plus the chunks it was built from.</summary>
public record AskResponse(string Answer, IReadOnlyList<SourceReference> Sources);

/// <summary>
/// A retrieved chunk that was shown to the model, with its similarity score.
///
/// TWEAK: add page numbers, section titles, or figure references here once
///        chunks carry that metadata (useful for citations in a UI).
/// </summary>
public record SourceReference(
    int Index,
    string DocumentTitle,
    string SourcePath,
    int ChunkIndex,
    string Snippet,
    double Score);
