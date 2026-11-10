using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using UmamusumeRag.Core.Abstractions;
using UmamusumeRag.Core.Models;

namespace UmamusumeRag.Core.Services;

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
    private const int SnippetLength = 300;

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
        // 1. Embed the question with the same model that embedded the chunks.
        var queryEmbeddings = await embeddingGenerator.GenerateAsync([question], cancellationToken: cancellationToken);
        var queryEmbedding = queryEmbeddings[0].Vector;

        // 2. Retrieve the closest chunks from pgvector.
        var k = topK ?? options.TopK;
        var chunks = await repository.SearchChunksAsync(queryEmbedding, k, cancellationToken);
        logger.LogInformation("Retrieved {Count} chunks for question (topK={TopK})", chunks.Count, k);

        if (chunks.Count == 0)
        {
            return new AskResponse(
                "I don't have any indexed documents to answer from yet. " +
                "Ingest some documents first (POST /api/documents/ingest).",
                []);
        }

        // 3. Build a grounded prompt: the numbered context is all the model is allowed to use.
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

        // 4. Generate the answer.
        var response = await chatClient.GetResponseAsync(
            messages,
            new ChatOptions { Temperature = 0.1f },
            cancellationToken);

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
