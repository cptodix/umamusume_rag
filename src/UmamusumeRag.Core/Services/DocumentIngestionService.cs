using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using UmamusumeRag.Core.Abstractions;
using UmamusumeRag.Core.Entities;
using UmamusumeRag.Core.Models;

namespace UmamusumeRag.Core.Services;

// ===== RAG write path · steps 1-4 ============================================
// This service is the "ETL job" of the system. For each input file:
//
//   1. LOAD    file bytes  -> plain text        (IDocumentLoader)
//   2. CHUNK   text        -> chunk strings     (ITextChunker)
//   3. EMBED   chunks      -> float[] vectors   (IEmbeddingGenerator -> LLM server)
//   4. STORE   everything  -> PostgreSQL        (IDocumentRepository)
//
// Steps 1-2 are cheap and local. Step 3 is the expensive, network-bound one —
// which is why an unchanged file is skipped before we ever get there.
//
// TWEAK: this is where to add ingestion-side features: deduplication, metadata,
//        figure captioning, batching/parallelism, progress reporting.
// ============================================================================

/// <summary>
/// The write side of the RAG pipeline:
/// load file -> chunk text -> embed chunks -> store document + chunks.
/// </summary>
public sealed class DocumentIngestionService(
    IEnumerable<IDocumentLoader> loaders,
    ITextChunker chunker,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IDocumentRepository repository,
    RagOptions options,
    ILogger<DocumentIngestionService> logger)
{
    /// <summary>
    /// Scans a folder (recursively) and ingests every file that some registered
    /// loader claims to support. Unsupported files are skipped silently.
    ///
    /// TWEAK: the filters below decide what counts as an input document. For
    ///        example, remove the "~$" filter to stop ignoring Office lock files,
    ///        or add a size limit / extension allow-list here.
    /// </summary>
    public async Task<IReadOnlyList<IngestResult>> IngestDirectoryAsync(
        string directory,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var files = Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(file => !Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal)) // Word/Office lock files
            .Where(file => loaders.Any(loader => loader.CanLoad(file))) // <-- only formats we have a loader for
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase);

        // Sequential on purpose: embedding servers are often single-model and
        // rate-limited. TWEAK: parallelize with a bounded degree if your
        // endpoint can take it (e.g. Parallel.ForEachAsync + SemaphoreSlim).
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
    ///
    /// Failures are captured per file (returned as <see cref="IngestStatus.Failed"/>)
    /// so one bad document does not abort the whole batch.
    /// </summary>
    public async Task<IngestResult> IngestFileAsync(
        string filePath,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(filePath);

        try
        {
            // ---- RAG step 1/8 · LOAD -------------------------------------
            // Pick the first registered loader that understands this extension.
            // TWEAK: a PDF/image/OCR loader would plug in here with zero changes
            //        to the rest of the pipeline.
            var loader = loaders.FirstOrDefault(l => l.CanLoad(fullPath))
                ?? throw new NotSupportedException(
                    $"No document loader supports files like '{Path.GetExtension(fullPath)}'.");

            var loaded = await loader.LoadAsync(fullPath, cancellationToken);
            if (string.IsNullOrWhiteSpace(loaded.Content))
                return new IngestResult(fullPath, IngestStatus.Failed, 0, "No text could be extracted from the file.");

            // ---- Change detection (before spending money on embeddings) ---
            // The hash is over the *extracted text*, not the file bytes, so
            // re-saving a file without changing its content stays a no-op.
            //
            // GOTCHA: after changing the embedding model, the chunker, the
            //         prefixes or the dimensions you MUST pass force=true,
            //         otherwise this check keeps the old vectors.
            var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(loaded.Content)));

            var existing = await repository.FindBySourcePathAsync(fullPath, cancellationToken);
            if (!force && existing is not null && existing.ContentHash == contentHash)
            {
                logger.LogInformation("Skipped {Path}: content unchanged", fullPath);
                return new IngestResult(fullPath, IngestStatus.Skipped, existing.Chunks.Count,
                    "Content unchanged since the last ingest.");
            }

            // ---- RAG step 2/8 · CHUNK ------------------------------------
            // TWEAK: swap ITextChunker (DI registration) to change the strategy,
            //        or Rag:ChunkSize / Rag:ChunkOverlap for sizes.
            var chunkTexts = chunker.Chunk(loaded.Content);
            if (chunkTexts.Count == 0)
                return new IngestResult(fullPath, IngestStatus.Failed, 0, "Chunking produced no chunks.");

            // ---- RAG step 3/8 · EMBED (ingest side) ----------------------
            // One batched request for all chunks of this document. The model is
            // configured by Llm:EmbeddingModel — it MUST be the same model used
            // later to embed questions, otherwise the vector spaces don't match.
            //
            // Rag:DocumentPrefix exists for models trained with asymmetric
            // prefixes (E5 "passage: ", nomic "search_document: "). It stays
            // empty for BGE-M3/Qwen3. Rag:EmbeddingDimensions optionally
            // truncates the vector (Matryoshka) to shrink storage.
            //
            // TWEAK: batching size (some providers cap inputs per request),
            //        retry/backoff, or a local ONNX embedding model instead.
            logger.LogInformation("Embedding {Count} chunks from {Path}", chunkTexts.Count, fullPath);

            IEnumerable<string> embeddingTexts = options.DocumentPrefix.Length == 0
                ? chunkTexts
                : chunkTexts.Select(text => options.DocumentPrefix + text);

            var embeddingOptions = options.EmbeddingDimensions is { } dimensions
                ? new EmbeddingGenerationOptions { Dimensions = dimensions }
                : null;

            var embeddings = await embeddingGenerator.GenerateAsync(embeddingTexts, embeddingOptions, cancellationToken);
            if (embeddings.Count != chunkTexts.Count)
                throw new InvalidOperationException(
                    $"Embedding provider returned {embeddings.Count} vectors for {chunkTexts.Count} chunks.");

            // Fail loudly if the provider ignored the requested truncation —
            // silently storing the native width would break every later search.
            if (options.EmbeddingDimensions is { } expectedDimensions
                && embeddings.Count > 0
                && embeddings[0].Vector.Length != expectedDimensions)
            {
                throw new InvalidOperationException(
                    $"Embedding provider returned {embeddings[0].Vector.Length} dimensions, but " +
                    $"Rag:EmbeddingDimensions is {expectedDimensions}. This provider/model may not support truncation.");
            }

            // ---- RAG step 4/8 · STORE ------------------------------------
            // Build the aggregate and hand it to the repository, which replaces
            // any previous version of this file (chunks cascade-delete).
            //
            // TWEAK: this is where to add per-chunk metadata (page number,
            //        section heading, figure kind) before it reaches the DB.
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
                        Embedding = embeddings[index].Vector.ToArray(), // ReadOnlyMemory<float> -> float[]
                    })
                    .ToList(),
            };

            await repository.UpsertAsync(document, cancellationToken);
            logger.LogInformation("Ingested {Path}: {Count} chunks", fullPath, chunkTexts.Count);

            return new IngestResult(fullPath, IngestStatus.Ingested, chunkTexts.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deliberately swallowed into the result: a batch ingest should
            // continue when one file fails (bad PDF, model down, ...).
            logger.LogError(ex, "Failed to ingest {Path}", fullPath);
            return new IngestResult(fullPath, IngestStatus.Failed, 0, ex.Message);
        }
    }
}
