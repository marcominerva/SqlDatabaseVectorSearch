using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace SqlDatabaseVectorSearch.ContentDecoders;

public class DocxContentDecoder : IContentDecoder
{
    public Task<string> DecodeAsync(Stream stream, string contentType)
    {
        // Open a Word document for read-only access.
        using var document = WordprocessingDocument.Open(stream, false);

        var body = document.MainDocumentPart?.Document.Body;
        var content = new StringBuilder();

        var paragraphs = body?.Descendants<Paragraph>() ?? [];
        foreach (var p in paragraphs)
        {
            content.AppendLine(p.InnerText);
        }

        return Task.FromResult(content.ToString());
    }
}
