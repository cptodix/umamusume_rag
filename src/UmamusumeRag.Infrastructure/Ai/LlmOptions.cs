namespace UmamusumeRag.Infrastructure.Ai;

/// <summary>
/// Connection settings for an OpenAI-compatible API, bound from the "Llm"
/// configuration section. Works with LM Studio (default), OpenAI, OpenRouter,
/// or any other OpenAI-compatible endpoint.
/// </summary>
public class LlmOptions
{
    /// <summary>Base URL of the OpenAI-compatible API. LM Studio default: http://localhost:1234/v1</summary>
    public string BaseUrl { get; set; } = "http://localhost:1234/v1";

    /// <summary>API key. LM Studio ignores the value, but the client requires a non-empty one.</summary>
    public string ApiKey { get; set; } = "lm-studio";

    /// <summary>Model used to generate answers (e.g. "qwen/qwen3-8b" in LM Studio).</summary>
    public string ChatModel { get; set; } = string.Empty;

    /// <summary>Model used to embed chunks and questions (e.g. "text-embedding-nomic-embed-text-v1.5").</summary>
    public string EmbeddingModel { get; set; } = string.Empty;
}
