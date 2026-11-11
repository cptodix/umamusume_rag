using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using UmamusumeRag.Core.Abstractions;
using UmamusumeRag.Core.Models;

namespace UmamusumeRag.Core.Services;

// ===== RAG read path · steps 5-8 ============================================
// The whole "answer a question" flow, in order:
//
//   5. EMBED QUESTION  question      -> float[] vector
//   6. RETRIEVE        vector        -> top-K chunks (cosine similarity)
//   7. PROMPT          chunks+question -> chat messages (the "open book")
//   8. GENERATE        messages      -> answer text + sources
//
// Mental model: an open-book exam. Steps 5-6 find the right pages, step 7 hands
// those pages to the model, step 8 is the model writing the answer from them.
//
// The prompt in step 7 is where most "the answer is wrong" bugs are fixed;
// steps 5-6 are where most "the answer is missing" bugs are fixed.
//
// TWEAK: everything worth experimenting with is marked with TWEAK below.
// ============================================================================

/// <summary>
/// The read side of the RAG pipeline:
/// embed question -> retrieve closest chunks -> build prompt -> generate answer.
/// </summary>
public sealed class RagQueryService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IChatClient chatClient,
    IDocumentRepository repository,
    RagOptions options,
    ILogger<RagQueryService> logger)
{
    /// <summary>How many characters of each retrieved chunk are echoed back in the API response.</summary>
    private const int SnippetLength = 300;

    /// <summary>
    /// The grounding instructions. This is the single most influential string
    /// in the whole pipeline: it decides whether the model answers from the
    /// context, cites it, or invents something.
    ///
    /// TWEAK: try stricter ("If the answer is not in the context, reply exactly
    ///        'Not in the provided documents.'"), or looser wording, or force a
    ///        language/style. See docs/02-next-steps.md §3.3.
    /// </summary>
    private const string SystemPrompt = """
        You are a helpful assistant that answers questions about Umamusume: Pretty Derby (Global).

        Answer the question using ONLY the numbered context passages below.
        - Cite the passages you used with their number, like [1] or [2].
        - If the context does not contain the answer, say so plainly instead of guessing.
        - Keep answers concise and factual.
        """;

    public async Task<AskResponse> AskAsync(
        string question,
        int? topK = null,
        CancellationToken cancellationToken = default)
    {
        // ---- RAG step 5/8 · EMBED THE QUESTION ---------------------------
        // The question must be embedded with the SAME model that embedded the
        // chunks (Llm:EmbeddingModel). A mismatch produces meaningless scores
        // without any error.
        var queryEmbeddings = await embeddingGenerator.GenerateAsync([question], cancellationToken: cancellationToken);
        var queryEmbedding = queryEmbeddings[0].Vector;

        // ---- RAG step 6/8 · RETRIEVE -------------------------------------
        // Cosine search in PostgreSQL (pgvector `<=>`), best-first, top K.
        //
        // TWEAK: K comes from Rag:TopK, or per request via AskRequest.TopK.
        //        Add hybrid search / reranking inside or around this call —
        //        see PgVectorChunkRepository.SearchChunksAsync.
        var k = topK ?? options.TopK;
        var chunks = await repository.SearchChunksAsync(queryEmbedding, k, cancellationToken);
        logger.LogInformation("Retrieved {Count} chunks for question (topK={TopK})", chunks.Count, k);

        // Nothing indexed yet: return a useful hint instead of calling the model.
        if (chunks.Count == 0)
        {
            return new AskResponse(
                "I don't have any indexed documents to answer from yet. " +
                "Ingest some documents first (POST /api/documents/ingest).",
                []);
        }

        // ---- RAG step 7/8 · BUILD THE PROMPT -----------------------------
        // Numbered context, one block per chunk, labelled with its document
        // title so the model can attribute claims. The numbers here are the
        // same numbers used in the citations and in the response sources.
        //
        // TWEAK: ordering matters — models attend most to the start and end of
        //        long contexts ("lost in the middle"). Try reversing the order,
        //        adding section headings, or trimming per-chunk length.
        var context = string.Join("\n\n", chunks.Select((chunk, i) => $"[{i + 1}] (from \"{chunk.DocumentTitle}\")\n{chunk.Content}"));

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, $"""
                Context:
                {context}

                Question: {question}
                """),
        };

        // ---- RAG step 8/8 · GENERATE -------------------------------------
        // One chat completion call. Temperature 0.1 keeps answers close to the
        // context (RAG wants factuality, not creativity).
        //
        // TWEAK: Temperature, MaxOutputTokens, model (Llm:ChatModel), or add
        //        streaming here (IChatClient.GetStreamingResponseAsync).
        var response = await chatClient.GetResponseAsync(
            messages,
            new ChatOptions { Temperature = 0.1f },
            cancellationToken);

        // ---- Response shaping: answer + the sources it was built from ----
        // The sources array is your retrieval debugger: if the right chunk is
        // not here, the problem is retrieval (steps 5-6), not the model.
        //
        // TWEAK: add page numbers, figure links, or full chunk text here once
        //        chunks carry that metadata.
        var sources = chunks
            .Select((chunk, i) => new SourceReference(
                i + 1,
                chunk.DocumentTitle,
                chunk.SourcePath,
                chunk.ChunkIndex,
                chunk.Content.Length <= SnippetLength ? chunk.Content : chunk.Content[..SnippetLength] + "...",
                Math.Round(chunk.Score, 4)))
            .ToList();

        return new AskResponse(response.Text ?? string.Empty, sources);
    }
}
