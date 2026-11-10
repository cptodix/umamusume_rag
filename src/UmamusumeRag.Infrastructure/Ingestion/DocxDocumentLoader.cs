using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UmamusumeRag.Core.Abstractions;

namespace UmamusumeRag.Infrastructure.Ingestion;

/// <summary>
/// Extracts text from Word documents (.docx) — the format you get from
/// Google Docs via "File -> Download -> Microsoft Word (.docx)".
/// </summary>
public class DocxDocumentLoader : IDocumentLoader
{
    public bool CanLoad(string filePath) =>
        string.Equals(Path.GetExtension(filePath), ".docx", StringComparison.OrdinalIgnoreCase);

    public Task<LoadedDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default) =>
        // The Open XML SDK is synchronous; run it on the thread pool.
        Task.Run(() =>
        {
            using var document = WordprocessingDocument.Open(filePath, false);

            var paragraphs = document.MainDocumentPart?.Document?.Body?.Descendants<Paragraph>() ?? [];
            var content = string.Join(
                "\n\n",
                paragraphs
                    .Select(paragraph => paragraph.InnerText.Trim())
                    .Where(text => text.Length > 0));

            var title = document.PackageProperties?.Title is { Length: > 0 } propertyTitle
                ? propertyTitle
                : Path.GetFileNameWithoutExtension(filePath);

            return new LoadedDocument(title, Path.GetFullPath(filePath), content);
        }, cancellationToken);
}
