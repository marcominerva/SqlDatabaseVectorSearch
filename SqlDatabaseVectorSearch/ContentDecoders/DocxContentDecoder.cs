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

        //foreach (var paragraph in body!.Elements<Paragraph>())
        //{
        //    foreach (var element in paragraph.Elements())
        //    {
        //        if (element is Run run)
        //        {
        //            DecodeTextFromRun(run);
        //        }
        //        else if (element is Hyperlink hyperlink)
        //        {
        //            foreach (var hyperlinkRun in hyperlink.Elements<Run>())
        //            {
        //                DecodeTextFromRun(hyperlinkRun);
        //            }

        //            //var hyperlinkUri = doc.MainDocumentPart.HyperlinkRelationships.FirstOrDefault(r => r.Id == hyperlink.Id)?.Uri;
        //            //if (hyperlinkUri is not null)
        //            //{
        //            //    content.Append($" ({hyperlinkUri})");
        //            //}
        //        }
        //    }

        //    content.AppendLine(); // Preserve whitespace and blank lines.
        //}

        //return Task.FromResult(content.ToString());

        //void DecodeTextFromRun(Run run)
        //{
        //    foreach (var text in run.Elements<Text>())
        //    {
        //        content.Append(text.Text);
        //    }
        //}
    }
}
