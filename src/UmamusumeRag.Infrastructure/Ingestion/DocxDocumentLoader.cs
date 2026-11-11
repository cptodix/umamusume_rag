using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UmamusumeRag.Core.Abstractions;

namespace UmamusumeRag.Infrastructure.Ingestion;

// ===== RAG step 1/8 · LOAD (.docx) ==========================================
// Extracts paragraph text from Word documents — the format you get from
// Google Docs via "File -> Download -> Microsoft Word (.docx)".
//
// LIMITATION (text-only pipeline): images, native charts and text baked into
// images are invisible here. Only paragraph text survives. To ingest figures
// you would add a captioning step (vision model -> description text) before
// chunking — see docs/01-what-was-built.md, "Deliberately not implemented".
//
// TWEAK: include/exclude tables, map Heading styles to Markdown headings so a
//        structure-aware chunker can use them, or extract images via
//        MainDocumentPart.ImageParts for captioning.
// ============================================================================

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

            // Descendants<Paragraph> includes paragraphs inside tables, which is
            // usually what you want. Each paragraph is separated by a blank line
            // so the chunker's "\n\n" rule treats them as natural boundaries.
            var paragraphs = document.MainDocumentPart?.Document?.Body?.Descendants<Paragraph>() ?? [];
            var content = string.Join(
                "\n\n",
                paragraphs
                    .Select(paragraph => paragraph.InnerText.Trim())
                    .Where(text => text.Length > 0));

            // Prefer the document's own title metadata; fall back to the file name.
            var title = document.PackageProperties?.Title is { Length: > 0 } propertyTitle
                ? propertyTitle
                : Path.GetFileNameWithoutExtension(filePath);

            return new LoadedDocument(title, Path.GetFullPath(filePath), content);
        }, cancellationToken);
}
