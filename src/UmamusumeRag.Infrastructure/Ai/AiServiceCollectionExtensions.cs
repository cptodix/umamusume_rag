using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace UmamusumeRag.Infrastructure.Ai;

public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IChatClient"/> and <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>
    /// against any OpenAI-compatible endpoint (LM Studio, OpenAI, ...). Point
    /// "Llm:BaseUrl" at another provider to switch — no code changes needed.
    /// </summary>
    public static IServiceCollection AddOpenAiCompatibleAi(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Llm").Get<LlmOptions>() ?? new LlmOptions();

        if (string.IsNullOrWhiteSpace(options.ChatModel))
            throw new InvalidOperationException("Llm:ChatModel is not configured (see appsettings.json).");
        if (string.IsNullOrWhiteSpace(options.EmbeddingModel))
            throw new InvalidOperationException("Llm:EmbeddingModel is not configured (see appsettings.json).");

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(options.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(options.BaseUrl) });

        services.AddSingleton(openAiClient);
        services.AddSingleton<IChatClient>(openAiClient.GetChatClient(options.ChatModel).AsIChatClient());
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            openAiClient.GetEmbeddingClient(options.EmbeddingModel).AsIEmbeddingGenerator());

        return services;
    }
}
