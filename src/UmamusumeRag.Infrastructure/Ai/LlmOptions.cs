namespace UmamusumeRag.Infrastructure.Ai;

/// <summary>
/// Connection settings for an OpenAI-compatible API, bound from the "Llm"
/// configuration section in src/UmamusumeRag.Api/appsettings.json.
///
/// Switching providers is a config-only change:
///   LM Studio  -> BaseUrl http://localhost:1234/v1,  ApiKey anything
///   Ollama     -> BaseUrl http://localhost:11434/v1, ApiKey anything
///   OpenAI     -> BaseUrl https://api.openai.com/v1,  ApiKey sk-...
///
/// GOTCHA: ChatModel and EmbeddingModel must be two different models (a chat
/// model cannot embed, an embedding model cannot chat). And the embedding
/// model must stay the same for ingestion AND querying.
/// </summary>
public class LlmOptions
{
    /// <summary>Base URL of the OpenAI-compatible API. LM Studio default: http://localhost:1234/v1</summary>
    public string BaseUrl { get; set; } = "http://localhost:1234/v1";

    /// <summary>API key. LM Studio ignores the value, but the client requires a non-empty one.</summary>
    public string ApiKey { get; set; } = "lm-studio";

    /// <summary>
    /// Model used to generate answers (RAG step 8), e.g. "qwen/qwen3-8b".
    /// TWEAK: answer quality/grounding lives here more than anywhere else.
    /// </summary>
    public string ChatModel { get; set; } = string.Empty;

    /// <summary>
    /// Model used to embed chunks and questions (RAG steps 3 and 5), e.g.
    /// "text-embedding-nomic-embed-text-v1.5".
    ///
    /// TWEAK: changing this invalidates every stored vector — re-ingest with
    ///        ?force=true afterwards. See docs/02-next-steps.md §3.4.
    /// </summary>
    public string EmbeddingModel { get; set; } = string.Empty;
}
