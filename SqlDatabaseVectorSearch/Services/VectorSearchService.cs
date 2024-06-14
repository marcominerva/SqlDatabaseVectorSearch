using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Text;
using SqlDatabaseVectorSearch.DataAccessLayer;
using SqlDatabaseVectorSearch.DataAccessLayer.Entities;
using SqlDatabaseVectorSearch.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace SqlDatabaseVectorSearch.Services;

public class VectorSearchService(ApplicationDbContext dbContext, ITextEmbeddingGenerationService textEmbeddingGenerationService, ChatService chatService)
{
    public async Task<Guid> ImportAsync(Stream stream, string name, Guid? documentId)
    {
        // Extract the contents of the file (current, only PDF are supported).
        var content = await GetContentAsync(stream);

        if (documentId.HasValue)
        {
            // If the user is importing a document that already exists, delete the previous one.
            await DeleteDocumentAsync(documentId.Value);
        }
        else
        {
            // Creates a new document.
            documentId = Guid.NewGuid();
        }

        var document = new Document { Id = documentId.Value, Name = name, CreationDate = DateTimeOffset.UtcNow };
        dbContext.Documents.Add(document);

        // Split the content into chunks of at most 1024 tokens and generate the embeddings for each one.
        var paragraphs = TextChunker.SplitPlainTextParagraphs(TextChunker.SplitPlainTextLines(content, 300), 1024, 100);
        var embeddings = await textEmbeddingGenerationService.GenerateEmbeddingsAsync(paragraphs);

        foreach (var (paragraph, embedding) in paragraphs.Zip(embeddings, (p, e) => (p, e.ToArray())))
        {
            var documentChunk = new DocumentChunk { DocumentId = documentId.Value, Content = paragraph, Embedding = embedding };
            dbContext.DocumentChunks.Add(documentChunk);
        }

        await dbContext.SaveChangesAsync();
        return documentId.Value;
    }

    public async Task DeleteDocumentAsync(Guid documentId)
    {
        var document = await dbContext.Documents.Include(d => d.DocumentChunks).FirstOrDefaultAsync(d => d.Id == documentId);
        if (document is null)
        {
            return;
        }

        dbContext.DocumentChunks.RemoveRange(document.DocumentChunks);
        dbContext.Documents.Remove(document);

        await dbContext.SaveChangesAsync();
    }

    public async Task<Response?> AskQuestionAsync(Question question, bool reformulate = true)
    {
        // Reformulate the following question taking into account the context of the chat to perform keyword search and embeddings:
        var reformulatedQuestion = reformulate ? await chatService.CreateQuestionAsync(question.ConversationId, question.Text) : question.Text;

        // Perform Vector Search on SQL Server.
        var questionEmbedding = await textEmbeddingGenerationService.GenerateEmbeddingAsync(reformulatedQuestion);

        var chunks = await dbContext.DocumentChunks
            .OrderBy(c => EF.Functions.VectorDistance("cosine", c.Embedding, questionEmbedding.ToArray()))
            //.Select(c => new
            //{
            //    c.Id,
            //    c.DocumentId,
            //    c.Content,
            //    Distance = EF.Functions.VectorDistance("cosine", c.Embedding, questionEmbedding.ToArray())
            //})
            .Take(5)
            .ToListAsync();

        var answer = await chatService.AskQuestionAsync(question.ConversationId, chunks, reformulatedQuestion);
        return new Response(reformulatedQuestion, answer);
    }

    //public async Task<SearchResult?> SearchAsync(Search search, double minimumRelevance = 0, string? index = null)
    //{
    //    // Search using the embedding search via Kernel Memory .
    //    // If tags are provided, use them as filters with OR logic.
    //    var searchResult = await memory.SearchAsync(search.Text.TrimEnd([' ', '?']), index, filters: search.Tags.ToMemoryFilters(), minRelevance: minimumRelevance, limit: 50);

    //    // If you want to use an AND logic, set the "filter" parameter (instead of "filters").
    //    //var searchResult = await memory.SearchAsync(search.Text.TrimEnd([' ', '?']), index, filter: search.Tags.ToMemoryFilter(), minRelevance: minimumRelevance);

    //    return searchResult;
    //}

    private static Task<string> GetContentAsync(Stream stream)
    {
        var content = new StringBuilder();

        // Reads the content of the PDF document using PdfPig.
        using var pdfDocument = PdfDocument.Open(stream);

        foreach (var page in pdfDocument.GetPages().Where(x => x != null))
        {
            var pageContent = ContentOrderTextExtractor.GetText(page) ?? string.Empty;
            content.AppendLine(pageContent);
        }

        return Task.FromResult(content.ToString());
    }
}