using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace SqlDatabaseVectorSearch.ContentDecoders;

public class PdfContentDecoder : IContentDecoder
{
    public Task<string> DecodeAsync(Stream stream, string contentType, CancellationToken cancellationToken = default)
    {
        var content = new StringBuilder();

        // Read the content of the PDF document.
        using var pdfDocument = PdfDocument.Open(stream);

        foreach (var page in pdfDocument.GetPages().Where(x => x is not null))
        {
            var pageContent = ContentOrderTextExtractor.GetText(page) ?? string.Empty;
            content.AppendLine(pageContent);
        }

        return Task.FromResult(content.ToString());
    }
}
