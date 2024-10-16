using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Text;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Settings;
using TinyHelpers.Extensions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace SqlDatabaseVectorSearch.Services;

public class VectorSearchService(SqlConnection sqlConnection, ITextEmbeddingGenerationService textEmbeddingGenerationService, ChatService chatService, TimeProvider timeProvider, IOptions<AppSettings> appSettingsOptions)
{
    private readonly AppSettings appSettings = appSettingsOptions.Value;

    public async Task<Guid> ImportAsync(Stream stream, string name, Guid? documentId)
    {
        // Extract the contents of the file (currently, only PDF files are supported).
        var content = await GetContentAsync(stream);

        await sqlConnection.OpenAsync();
        await using var transaction = await sqlConnection.BeginTransactionAsync();

        if (documentId.HasValue)
        {
            // If the user is importing a document that already exists, delete the previous one.
            await DeleteDocumentAsync(documentId.Value, transaction);
        }

        documentId = await sqlConnection.ExecuteScalarAsync<Guid>($"""
            INSERT INTO Documents (Id, [Name], CreationDate)
            OUTPUT INSERTED.Id
            VALUES (@Id, @Name, @CreationDate);
            """, new { Id = documentId.GetValueOrDefault(Guid.NewGuid()), Name = name, CreationDate = timeProvider.GetUtcNow() },
            transaction);

        // Split the content into chunks and generate the embeddings for each one.
        var paragraphs = TextChunker.SplitPlainTextParagraphs(TextChunker.SplitPlainTextLines(content, appSettings.MaxTokensPerLine), appSettings.MaxTokensPerParagraph, appSettings.OverlapTokens);
        var embeddings = await textEmbeddingGenerationService.GenerateEmbeddingsAsync(paragraphs);

        // Save the document chunks and the corresponding embedding in the database.
        foreach (var (paragraph, index) in paragraphs.WithIndex())
        {
            await sqlConnection.ExecuteAsync($"""
                INSERT INTO DocumentChunks (DocumentId, [Index], Content, Embedding)
                VALUES (@DocumentId, @Index, @Content, CAST(@Embedding AS VECTOR({embeddings[index].Length})));
                """, new { DocumentId = documentId, Index = index, Content = paragraph, Embedding = JsonSerializer.Serialize(embeddings[index]) },
                transaction);
        }

        await transaction.CommitAsync();

        return documentId.Value;
    }

    public async Task<IEnumerable<Document>> GetDocumentsAsync()
    {
        var documents = await sqlConnection.QueryAsync<Document>("""
            SELECT Id, [Name], CreationDate, ChunkCount = (SELECT COUNT(*) FROM DocumentChunks WHERE DocumentId = Documents.Id)
            FROM Documents
            ORDER BY [Name];
            """);

        return documents;
    }

    public async Task<IEnumerable<DocumentChunk>> GetDocumentChunksAsync(Guid documentId)
    {
        var documentChunks = await sqlConnection.QueryAsync<DocumentChunk>("""
            SELECT Id, [Index], Content
            FROM DocumentChunks
            WHERE DocumentId = @DocumentId
            ORDER BY [Index];
            """, new { documentId });

        return documentChunks;
    }

    public async Task<DocumentChunk?> GetDocumentChunkEmbeddingAsync(Guid documentId, Guid documentChunkId)
    {
        var documentChunk = await sqlConnection.QueryFirstOrDefaultAsync<DocumentChunk>("""
            SELECT Id, [Index], Content, CAST(Embedding AS NVARCHAR(MAX)) AS Embedding
            FROM DocumentChunks
            WHERE Id = @DocumentChunkId AND DocumentId = @DocumentId;
            """, new { documentId, documentChunkId });

        return documentChunk;
    }

    public Task DeleteDocumentAsync(Guid documentId, DbTransaction? transaction = null)
        => sqlConnection.ExecuteAsync("DELETE FROM Documents WHERE Id = @DocumentId", new { DocumentId = documentId }, transaction);

    public async Task<Response> AskQuestionAsync(Question question, bool reformulate = true)
    {
        // Reformulate the following question taking into account the context of the chat to perform keyword search and embeddings:
        var reformulatedQuestion = reformulate ? await chatService.CreateQuestionAsync(question.ConversationId, question.Text) : question.Text;

        // Perform Vector Search on SQL Database.
        var questionEmbedding = await textEmbeddingGenerationService.GenerateEmbeddingAsync(reformulatedQuestion);

        var chunks = await sqlConnection.QueryAsync<string>($"""
            SELECT TOP (@MaxRelevantChunks) Content
            FROM DocumentChunks
            ORDER BY VECTOR_DISTANCE('cosine', Embedding, CAST(@QuestionEmbedding AS VECTOR({questionEmbedding.Length})));
            """, new { appSettings.MaxRelevantChunks, QuestionEmbedding = JsonSerializer.Serialize(questionEmbedding) });

        var answer = await chatService.AskQuestionAsync(question.ConversationId, chunks, reformulatedQuestion);
        return new Response(reformulatedQuestion, answer);
    }

    private static Task<string> GetContentAsync(Stream stream)
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