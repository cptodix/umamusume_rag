namespace UmamusumeRag.Core.Abstractions;

/// <summary>
/// Splits a document's text into smaller pieces that fit the embedding model
/// and the generation model's context window.
/// </summary>
public interface ITextChunker
{
    IReadOnlyList<string> Chunk(string text);
}
