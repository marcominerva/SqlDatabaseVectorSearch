using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Text;
using SqlDatabaseVectorSearch.DataAccessLayer;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Settings;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Entities = SqlDatabaseVectorSearch.DataAccessLayer.Entities;

namespace SqlDatabaseVectorSearch.Services;

public class VectorSearchService(ApplicationDbContext dbContext, ITextEmbeddingGenerationService textEmbeddingGenerationService, ChatService chatService, IOptions<AppSettings> appSettingsOptions)
{
    private readonly AppSettings appSettings = appSettingsOptions.Value;

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
            // Create a new document.
            documentId = Guid.NewGuid();
        }

        var document = new Entities.Document { Id = documentId.Value, Name = name, CreationDate = DateTimeOffset.UtcNow };
        dbContext.Documents.Add(document);

        // Split the content into chunks and generate the embeddings for each one.
        var paragraphs = TextChunker.SplitPlainTextParagraphs(TextChunker.SplitPlainTextLines(content, appSettings.MaxTokensPerLine), appSettings.MaxTokensPerParagraph, appSettings.OverlapTokens);
        var embeddings = await textEmbeddingGenerationService.GenerateEmbeddingsAsync(paragraphs);

        foreach (var (paragraph, embedding) in paragraphs.Zip(embeddings, (p, e) => (p, e.ToArray())))
        {
            var documentChunk = new Entities.DocumentChunk { DocumentId = documentId.Value, Content = paragraph, Embedding = embedding };
            dbContext.DocumentChunks.Add(documentChunk);
        }

        await dbContext.SaveChangesAsync();
        return documentId.Value;
    }

    public async Task<IEnumerable<Document>> GetDocumentsAsync()
    {
        var documents = await dbContext.Documents.OrderBy(d => d.Name).AsNoTracking()
            .Select(d => new Document(d.Id, d.Name, d.CreationDate, d.DocumentChunks.Count))
            .ToListAsync();

        return documents;
    }

    public async Task DeleteDocumentAsync(Guid documentId)
    {
        await DeleteDocumentInternalAsync(documentId);
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
            .Take(appSettings.MaxChunksCount)
            .ToListAsync();

        var answer = await chatService.AskQuestionAsync(question.ConversationId, chunks, reformulatedQuestion);
        return new Response(reformulatedQuestion, answer);
    }

    private static Task<string> GetContentAsync(Stream stream)
    {
        var content = new StringBuilder();

        // Reads the content of the PDF document using PdfPig.
        using var pdfDocument = PdfDocument.Open(stream);

        foreach (var page in pdfDocument.GetPages().Where(x => x is not null))
        {
            var pageContent = ContentOrderTextExtractor.GetText(page) ?? string.Empty;
            content.AppendLine(pageContent);
        }

        return Task.FromResult(content.ToString());
    }

    private async Task DeleteDocumentInternalAsync(Guid documentId)
    {
        var document = await dbContext.Documents.Include(d => d.DocumentChunks).FirstOrDefaultAsync(d => d.Id == documentId);
        if (document is null)
        {
            return;
        }

        dbContext.DocumentChunks.RemoveRange(document.DocumentChunks);
        dbContext.Documents.Remove(document);
    }
}