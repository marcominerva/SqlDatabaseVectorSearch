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

        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document.Body;
        if (body is null)
        {
            return Task.FromResult(Enumerable.Empty<Chunk>());
        }

        var pages = new List<string>();
        var pageBuilder = new StringBuilder();

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            // Note: this is just an attempt at counting pages, not 100% reliable
            // see https://stackoverflow.com/questions/39992870/how-to-access-openxml-content-by-page-number
            var lastRenderedPageBreak = paragraph.GetFirstChild<Run>()?.GetFirstChild<LastRenderedPageBreak>();
            if (lastRenderedPageBreak is not null)
            {
                // Note: no trimming, use original spacing when working with pages
                pages.Add(pageBuilder.ToString());
                pageBuilder.Clear();
            }

            pageBuilder.AppendLine(paragraph.InnerText);
        }

        // Dopo aver processato tutti i paragrafi, aggiungi l'ultima pagina (anche se vuota)
        pages.Add(pageBuilder.ToString());

        var chunks = new List<Chunk>();
        foreach (var (pageIndex, pageText) in pages.Index())
        {
            var paragraphs = textChunker.Split(pageText.Trim());
            chunks.AddRange(paragraphs.Where(p => !string.IsNullOrWhiteSpace(p)).Select((text, index) => new Chunk(pageIndex + 1, index, text)));
        }

        return Task.FromResult(chunks.AsEnumerable());
    }
}
