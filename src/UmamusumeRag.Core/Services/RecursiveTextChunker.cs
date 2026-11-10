using System.Text;
using UmamusumeRag.Core.Abstractions;
using UmamusumeRag.Core.Models;

namespace UmamusumeRag.Core.Services;

/// <summary>
/// Splits text into overlapping chunks, preferring natural boundaries:
/// paragraphs first, then lines, then sentences, then words, and finally
/// a hard character split for anything longer than a single chunk.
/// </summary>
public sealed class RecursiveTextChunker : ITextChunker
{
    // Tried in order. An empty separator means "split anywhere" (hard split).
    private static readonly string[] Separators = ["\n\n", "\n", ". ", "? ", "! ", " ", ""];

    private readonly int _chunkSize;
    private readonly int _chunkOverlap;

    public RecursiveTextChunker(RagOptions options)
    {
        if (options.ChunkSize <= 0)
            throw new ArgumentException("Rag:ChunkSize must be greater than zero.");
        if (options.ChunkOverlap < 0 || options.ChunkOverlap >= options.ChunkSize)
            throw new ArgumentException("Rag:ChunkOverlap must be >= 0 and smaller than Rag:ChunkSize.");

        _chunkSize = options.ChunkSize;
        _chunkOverlap = options.ChunkOverlap;
    }

    public IReadOnlyList<string> Chunk(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Trim();
        if (normalized.Length == 0)
            return [];

        var splits = SplitRecursive(normalized, 0)
            .Select(split => split.Trim())
            .Where(split => split.Length > 0)
            .ToList();

        return ApplyOverlap(splits);
    }

    private List<string> SplitRecursive(string text, int separatorIndex)
    {
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
            var candidate = current.Length == 0 ? part : $"{current}{separator}{part}";

            if (candidate.Length <= _chunkSize)
            {
                current.Clear().Append(candidate);
                continue;
            }

            if (current.Length > 0)
            {
                result.Add(current.ToString());
                current.Clear();
            }

            if (part.Length > _chunkSize)
                result.AddRange(SplitRecursive(part, separatorIndex + 1));
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
        var parts = new List<string>();
        for (var i = 0; i < text.Length; i += _chunkSize)
            parts.Add(text.Substring(i, Math.Min(_chunkSize, text.Length - i)));
        return parts;
    }

    /// <summary>
    /// Prepends the tail of each chunk to the next one, so meaning is not lost
    /// at chunk boundaries. The overlap never starts mid-word.
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

            var firstSpace = tail.IndexOf(' ');
            if (firstSpace > 0)
                tail = tail[(firstSpace + 1)..];
            tail = tail.Trim();

            result.Add(tail.Length > 0 ? $"{tail}\n{chunks[i]}" : chunks[i]);
        }

        return result;
    }
}
