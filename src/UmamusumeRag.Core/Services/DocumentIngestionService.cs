using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using UmamusumeRag.Core.Abstractions;
using UmamusumeRag.Core.Entities;
using UmamusumeRag.Core.Models;

namespace UmamusumeRag.Core.Services;

/// <summary>
/// The write side of the RAG pipeline:
/// load file -> chunk text -> embed chunks -> store document + chunks.
/// </summary>
public sealed class DocumentIngestionService(
    IEnumerable<IDocumentLoader> loaders,
    ITextChunker chunker,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IDocumentRepository repository,
    ILogger<DocumentIngestionService> logger)
{
    /// <summary>Ingests every supported file under <paramref name="directory"/>, recursively.</summary>
    public async Task<IReadOnlyList<IngestResult>> IngestDirectoryAsync(
        string directory,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var files = Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(file => !Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal)) // Word/Office lock files
            .Where(file => loaders.Any(loader => loader.CanLoad(file)))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase);

        var results = new List<IngestResult>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await IngestFileAsync(file, force, cancellationToken));
        }

        return results;
    }

    /// <summary>
    /// Ingests a single file. Re-running on an unchanged file is a no-op unless
    /// <paramref name="force"/> is true.
    /// </summary>
    public async Task<IngestResult> IngestFileAsync(
        string filePath,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(filePath);

        try
        {
            var loader = loaders.FirstOrDefault(l => l.CanLoad(fullPath))
                ?? throw new NotSupportedException(
                    $"No document loader supports files like '{Path.GetExtension(fullPath)}'.");

            var loaded = await loader.LoadAsync(fullPath, cancellationToken);
            if (string.IsNullOrWhiteSpace(loaded.Content))
                return new IngestResult(fullPath, IngestStatus.Failed, 0, "No text could be extracted from the file.");

            var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(loaded.Content)));

            var existing = await repository.FindBySourcePathAsync(fullPath, cancellationToken);
            if (!force && existing is not null && existing.ContentHash == contentHash)
            {
                logger.LogInformation("Skipped {Path}: content unchanged", fullPath);
                return new IngestResult(fullPath, IngestStatus.Skipped, existing.Chunks.Count,
                    "Content unchanged since the last ingest.");
            }

            var chunkTexts = chunker.Chunk(loaded.Content);
            if (chunkTexts.Count == 0)
                return new IngestResult(fullPath, IngestStatus.Failed, 0, "Chunking produced no chunks.");

            logger.LogInformation("Embedding {Count} chunks from {Path}", chunkTexts.Count, fullPath);
            var embeddings = await embeddingGenerator.GenerateAsync(chunkTexts, cancellationToken: cancellationToken);
            if (embeddings.Count != chunkTexts.Count)
                throw new InvalidOperationException(
                    $"Embedding provider returned {embeddings.Count} vectors for {chunkTexts.Count} chunks.");

            var document = new Document
            {
                Title = loaded.Title,
                SourcePath = fullPath,
                ContentHash = contentHash,
                Chunks = chunkTexts
                    .Select((text, index) => new Chunk
                    {
                        ChunkIndex = index,
                        Content = text,
                        Embedding = embeddings[index].Vector.ToArray(),
                    })
                    .ToList(),
            };

            await repository.UpsertAsync(document, cancellationToken);
            logger.LogInformation("Ingested {Path}: {Count} chunks", fullPath, chunkTexts.Count);

            return new IngestResult(fullPath, IngestStatus.Ingested, chunkTexts.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to ingest {Path}", fullPath);
            return new IngestResult(fullPath, IngestStatus.Failed, 0, ex.Message);
        }
    }
}
