using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace UmamusumeRag.Infrastructure.Ai;

// ===== RAG steps 3/8, 5/8 and 8/8 · the model calls =========================
// All three model interactions in the pipeline go through Microsoft.Extensions.AI
// abstractions, created here from ONE OpenAI-compatible client:
//
//   step 3 (embed chunks)     IEmbeddingGenerator -> POST /v1/embeddings
//   step 5 (embed question)   IEmbeddingGenerator -> POST /v1/embeddings
//   step 8 (generate answer)  IChatClient         -> POST /v1/chat/completions
//
// Because the base URL is configuration, the same code works against LM Studio,
// Ollama, OpenAI, OpenRouter, vLLM, ... — no code change, only appsettings.
//
// TWEAK: switch providers in Llm:BaseUrl / Llm:ApiKey / Llm:*Model. For a
//        non-OpenAI-compatible backend, register a different IChatClient /
//        IEmbeddingGenerator implementation here instead.
// ============================================================================

public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// Registers <c>IChatClient</c> and <c>IEmbeddingGenerator</c> against any
    /// OpenAI-compatible endpoint (LM Studio, OpenAI, ...).
    /// </summary>
    public static IServiceCollection AddOpenAiCompatibleAi(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Llm").Get<LlmOptions>() ?? new LlmOptions();

        // Fail fast: a missing model name would otherwise surface as a confusing
        // 404 from the provider on the first ingest/ask.
        if (string.IsNullOrWhiteSpace(options.ChatModel))
            throw new InvalidOperationException("Llm:ChatModel is not configured (see appsettings.json).");
        if (string.IsNullOrWhiteSpace(options.EmbeddingModel))
            throw new InvalidOperationException("Llm:EmbeddingModel is not configured (see appsettings.json).");

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(options.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(options.BaseUrl) });

        services.AddSingleton(openAiClient);

        // Step 8: answer generation. Temperature etc. are set per call in
        // RagQueryService, not here, so experiments stay in the pipeline code.
        services.AddSingleton<IChatClient>(openAiClient.GetChatClient(options.ChatModel).AsIChatClient());

        // Steps 3 and 5: embeddings for chunks and for questions. ONE instance
        // is used by both sides on purpose — mismatched models silently break
        // similarity, so the pipeline must not be able to configure them apart.
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            openAiClient.GetEmbeddingClient(options.EmbeddingModel).AsIEmbeddingGenerator());

        return services;
    }
}
