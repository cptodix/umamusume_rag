using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace UmamusumeRag.Infrastructure.Messaging;

// ===== MassTransit mediator registration ===================================
// In-process only: no transport, no broker. Endpoints send a message and await
// the consumer's response (request/response over the mediator).
//
// TWEAK: to move to a real broker later, replace AddMediator with
//        AddMassTransit(x => { x.UsingRabbitMq(...); x.AddConsumer<...>(); })
//        and register IRequestClient<T> — the messages and consumers stay.
//
// LICENSE NOTE: MassTransit 8.x is Apache-2.0; v9+ is commercial. Keep the
// pinned 8.5.12 unless a commercial license is in place.
// ============================================================================

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the in-process mediator and the RAG consumers.
    /// Inject <c>IScopedMediator</c> (scoped) to send requests from endpoints.
    /// </summary>
    public static IServiceCollection AddRagMessaging(this IServiceCollection services)
    {
        services.AddMediator(cfg =>
        {
            cfg.AddConsumer<IngestDocumentsConsumer>();
            cfg.AddConsumer<IngestDocumentConsumer>();
            cfg.AddConsumer<AskQuestionConsumer>();
        });

        return services;
    }
}
