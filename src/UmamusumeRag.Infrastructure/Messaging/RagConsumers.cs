using MassTransit;
using Microsoft.Extensions.Logging;
using UmamusumeRag.Core.Messaging;
using UmamusumeRag.Core.Services;

namespace UmamusumeRag.Infrastructure.Messaging;

// ===== MassTransit mediator consumers ======================================
// Thin adapters between messages and the hand-rolled pipeline. All RAG logic
// stays in Core/Services; this layer only dispatches.
//
// The mediator is in-process: no broker, no serialization, and an exception in
// a consumer propagates back to the caller (the HTTP endpoint).
//
// TWEAK: add logging/metrics/validation filters here, or split a consumer into
//        smaller messages if the pipeline becomes asynchronous.
// ============================================================================

/// <summary>Handles <see cref="IngestDocuments"/> by scanning a folder (RAG steps 1-4).</summary>
public class IngestDocumentsConsumer(
    DocumentIngestionService ingestion,
    ILogger<IngestDocumentsConsumer> logger)
    : IConsumer<IngestDocuments>
{
    public async Task Consume(ConsumeContext<IngestDocuments> context)
    {
        logger.LogInformation(
            "Ingest command received: directory={Directory}, force={Force}",
            context.Message.Directory,
            context.Message.Force);

        var results = await ingestion.IngestDirectoryAsync(
            context.Message.Directory,
            context.Message.Force,
            context.CancellationToken);

        await context.RespondAsync(new IngestDocumentsResult { Results = results });
    }
}

/// <summary>Handles <see cref="IngestDocument"/> for a single file (RAG steps 1-4).</summary>
public class IngestDocumentConsumer(
    DocumentIngestionService ingestion,
    ILogger<IngestDocumentConsumer> logger)
    : IConsumer<IngestDocument>
{
    public async Task Consume(ConsumeContext<IngestDocument> context)
    {
        logger.LogInformation("Ingest command received: file={File}", context.Message.FilePath);

        var result = await ingestion.IngestFileAsync(
            context.Message.FilePath,
            context.Message.Force,
            context.CancellationToken);

        await context.RespondAsync(result);
    }
}

/// <summary>Handles <see cref="AskQuestion"/> by running the retrieval + generation pipeline (RAG steps 5-8).</summary>
public class AskQuestionConsumer(
    RagQueryService rag,
    ILogger<AskQuestionConsumer> logger)
    : IConsumer<AskQuestion>
{
    public async Task Consume(ConsumeContext<AskQuestion> context)
    {
        logger.LogInformation("Ask query received: topK={TopK}", context.Message.TopK);

        var response = await rag.AskAsync(
            context.Message.Question,
            context.Message.TopK,
            context.CancellationToken);

        await context.RespondAsync(response);
    }
}
