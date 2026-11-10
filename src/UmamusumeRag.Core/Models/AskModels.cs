namespace UmamusumeRag.Core.Models;

/// <summary>Request body for POST /api/ask.</summary>
public record AskRequest(string Question, int? TopK = null);

/// <summary>Answer generated from the retrieved context, plus the chunks it was built from.</summary>
public record AskResponse(string Answer, IReadOnlyList<SourceReference> Sources);

/// <summary>A retrieved chunk that was shown to the model, with its similarity score.</summary>
public record SourceReference(
    int Index,
    string DocumentTitle,
    string SourcePath,
    int ChunkIndex,
    string Snippet,
    double Score);
