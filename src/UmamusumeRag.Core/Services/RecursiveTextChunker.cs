using System.Text;
using UmamusumeRag.Core.Abstractions;
using UmamusumeRag.Core.Models;

namespace UmamusumeRag.Core.Services;

// ===== RAG step 2/8 · CHUNK (default implementation) =========================
// Algorithm: recursive splitting. It tries the most natural boundary first and
// only falls back to harsher ones when a piece is still too big:
//   paragraph (\n\n) -> line (\n) -> sentence (. ? !) -> word (space) -> hard cut
// Then it applies overlap so a sentence that straddles a boundary is still
// retrievable from both sides.
//
// WHY character-based, not token-based: no tokenizer dependency, and for
// English-ish text ~4 chars ≈ 1 token. Good enough to learn the trade-offs.
//
// TWEAK:
//   - Sizes:            Rag:ChunkSize / Rag:ChunkOverlap (appsettings.json)
//   - Boundary order:   the `Separators` array below
//   - Overlap strategy: `ApplyOverlap` (currently prepends the previous tail)
//   - Whole strategy:   implement ITextChunker and register your class instead
// ============================================================================

/// <summary>
/// Splits text into overlapping chunks, preferring natural boundaries:
/// paragraphs first, then lines, then sentences, then words, and finally
/// a hard character split for anything longer than a single chunk.
/// </summary>
public sealed class RecursiveTextChunker : ITextChunker
{
    // Tried in order. An empty separator means "split anywhere" (hard split).
    //
    // TWEAK: this list IS the chunking strategy. Examples:
    //   - Markdown-aware: split on "\n## " first so sections stay together.
    //   - Code/logs: remove ". " so you don't split mid-statement.
    //   - CJK text: add "。" "！" "？" (the current list is English-centric).
    private static readonly string[] Separators = ["\n\n", "\n", ". ", "? ", "! ", " ", ""];

    private readonly int _chunkSize;
    private readonly int _chunkOverlap;

    public RecursiveTextChunker(RagOptions options)
    {
        // Fail fast at startup rather than producing silently broken chunks.
        if (options.ChunkSize <= 0)
            throw new ArgumentException("Rag:ChunkSize must be greater than zero.");
        if (options.ChunkOverlap < 0 || options.ChunkOverlap >= options.ChunkSize)
            throw new ArgumentException("Rag:ChunkOverlap must be >= 0 and smaller than Rag:ChunkSize.");

        _chunkSize = options.ChunkSize;
        _chunkOverlap = options.ChunkOverlap;
    }

    public IReadOnlyList<string> Chunk(string text)
    {
        // Normalize line endings so separators match consistently.
        var normalized = text.Replace("\r\n", "\n").Trim();
        if (normalized.Length == 0)
            return [];

        var splits = SplitRecursive(normalized, 0)
            .Select(split => split.Trim())
            .Where(split => split.Length > 0)
            .ToList();

        // Overlap is applied after splitting so the recursive pass stays simple.
        return ApplyOverlap(splits);
    }

    /// <summary>
    /// Splits <paramref name="text"/> with the separator at
    /// <paramref name="separatorIndex"/>, merging small pieces back up to the
    /// chunk size and recursing with the next separator for oversized pieces.
    /// </summary>
    private List<string> SplitRecursive(string text, int separatorIndex)
    {
        // Base case: small enough, or no separators left (hard split already done).
        if (text.Length <= _chunkSize || separatorIndex >= Separators.Length)
            return [text];

        var separator = Separators[separatorIndex];
        var parts = separator.Length == 0
            ? HardSplit(text)
            : text.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var result = new List<string>();
        var current = new StringBuilder();

        foreach (var part in parts)
        {
            // Greedy merge: keep adding pieces until the next one would overflow.
            var candidate = current.Length == 0 ? part : $"{current}{separator}{part}";

            if (candidate.Length <= _chunkSize)
            {
                current.Clear().Append(candidate);
                continue;
            }

            // Overflow: flush what we have, then handle the piece that didn't fit.
            if (current.Length > 0)
            {
                result.Add(current.ToString());
                current.Clear();
            }

            if (part.Length > _chunkSize)
                result.AddRange(SplitRecursive(part, separatorIndex + 1)); // too big: try a harsher separator
            else
                current.Append(part);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    /// <summary>Splits a piece that is too long for any separator into fixed-size slices.</summary>
    private List<string> HardSplit(string text)
    {
        // TWEAK: this is the last resort. It can cut words/sentences in half;
        // that only happens when a single "word" exceeds the chunk size.
        var parts = new List<string>();
        for (var i = 0; i < text.Length; i += _chunkSize)
            parts.Add(text.Substring(i, Math.Min(_chunkSize, text.Length - i)));
        return parts;
    }

    /// <summary>
    /// Prepends the tail of each chunk to the next one, so meaning is not lost
    /// at chunk boundaries. The overlap never starts mid-word.
    ///
    /// WHY: retrieval matches on whole chunks. If the answer is split across
    ///      chunks A|B, neither A nor B alone contains the full sentence —
    ///      the overlap makes sure at least one chunk does.
    /// </summary>
    private List<string> ApplyOverlap(List<string> chunks)
    {
        if (_chunkOverlap == 0 || chunks.Count <= 1)
            return chunks;

        var result = new List<string>(chunks.Count) { chunks[0] };

        for (var i = 1; i < chunks.Count; i++)
        {
            var previous = chunks[i - 1];
            var tail = previous.Length <= _chunkOverlap ? previous : previous[^_chunkOverlap..];

            // Do not start the overlap in the middle of a word.
            var firstSpace = tail.IndexOf(' ');
            if (firstSpace > 0)
                tail = tail[(firstSpace + 1)..];
            tail = tail.Trim();

            result.Add(tail.Length > 0 ? $"{tail}\n{chunks[i]}" : chunks[i]);
        }

        return result;
    }
}
