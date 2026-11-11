using UmamusumeRag.Core.Models;

namespace UmamusumeRag.Core.Messaging;

// ===== RAG entry-point contracts (MassTransit mediator) =====================
// The HTTP endpoints no longer call the pipeline services directly: they send
// these messages through an in-process MassTransit mediator, and a consumer in
// Infrastructure adapts each message to the pipeline.
//
// WHY plain records here: Core must stay free of MassTransit. These are just
// POCO messages; the mediator passes them by reference (no serialization).
//
// TWEAK: add a message + consumer to expose another pipeline operation, or
//        switch the mediator for a real transport (RabbitMQ, Azure Service Bus)
//        without touching the endpoints — see AGENTS.md.
// ============================================================================

/// <summary>Command: ingest every supported file under a folder (RAG steps 1-4).</summary>
public record IngestDocuments
{
    /// <summary>Absolute folder to scan (the endpoint resolves relative paths first).</summary>
    public required string Directory { get; init; }

    /// <summary>Re-embed files even when their content hash is unchanged.</summary>
    public bool Force { get; init; }
}

/// <summary>Response for <see cref="IngestDocuments"/>: one result per processed file.</summary>
public record IngestDocumentsResult
{
    public required IReadOnlyList<IngestResult> Results { get; init; }
}

/// <summary>Command: ingest one already-saved file (RAG steps 1-4).</summary>
public record IngestDocument
{
    public required string FilePath { get; init; }

    public bool Force { get; init; }
}

/// <summary>Query: answer a question from the indexed documents (RAG steps 5-8).</summary>
public record AskQuestion
{
    public required string Question { get; init; }

    /// <summary>Overrides Rag:TopK for this request.</summary>
    public int? TopK { get; init; }
}
