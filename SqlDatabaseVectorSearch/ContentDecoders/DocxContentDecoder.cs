using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SqlDatabaseVectorSearch.TextChunkers;

namespace SqlDatabaseVectorSearch.ContentDecoders;

public class DocxContentDecoder(IServiceProvider serviceProvider) : IContentDecoder
{
    public Task<IEnumerable<Chunk>> DecodeAsync(Stream stream, string contentType, CancellationToken cancellationToken = default)
    {
        var textChunker = serviceProvider.GetRequiredKeyedService<ITextChunker>(contentType);

        // Open a Word document for read-only access.
        using var document = WordprocessingDocument.Open(stream, false);

        var body = document.MainDocumentPart?.Document.Body;
        var content = new StringBuilder();

        foreach (var p in body?.Descendants<Paragraph>() ?? [])
        {
            content.AppendLine(p.InnerText);
        }

        var paragraphs = textChunker.Split(content.ToString().Trim());

        // Pages do not exist in the OpenXML format until they are rendered by a word processor.
        // See https://stackoverflow.com/questions/43700252/how-to-get-page-numbers-based-on-openxmlelement for more details.
        // Therefore, we will not assign a page number.
        return Task.FromResult(paragraphs.Select((text, index) => new Chunk(null, index, text)).ToList().AsEnumerable());
    }
}
