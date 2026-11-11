namespace UmamusumeRag.Core.Abstractions;

// ===== RAG step 2/8 · CHUNK =================================================
// Splits a document into the pieces that are embedded, stored and later pasted
// into the prompt. The chunk is BOTH the unit of retrieval and the unit of
// context, so its size is a trade-off:
//   smaller -> matches questions precisely, but loses surrounding context
//   larger  -> keeps context, but dilutes the embedding and eats the model's
//              context window (and your token budget)
//
// TWEAK: implement this interface to try another strategy (sentence window,
//        markdown sections, token-based sizes, parent/child chunks, ...).
// ============================================================================

/// <summary>
/// Splits a document's text into smaller pieces that fit the embedding model
/// and the generation model's context window.
/// </summary>
public interface ITextChunker
{
    IReadOnlyList<string> Chunk(string text);
}
