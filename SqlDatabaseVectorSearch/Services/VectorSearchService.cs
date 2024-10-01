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

        await using var command = sqlConnection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;

        command.CommandText = """
            INSERT INTO Documents (Id, [Name], CreationDate)
            OUTPUT INSERTED.Id
            VALUES (@Id, @Name, @CreationDate);
            """;

        command.Parameters.AddWithValue("@Id", documentId.GetValueOrDefault(Guid.NewGuid()));
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@CreationDate", timeProvider.GetUtcNow());

        var insertedId = await command.ExecuteScalarAsync();
        documentId = (Guid)insertedId!;

        // Split the content into chunks and generate the embeddings for each one.
        var paragraphs = TextChunker.SplitPlainTextParagraphs(TextChunker.SplitPlainTextLines(content, appSettings.MaxTokensPerLine), appSettings.MaxTokensPerParagraph, appSettings.OverlapTokens);
        var embeddings = await textEmbeddingGenerationService.GenerateEmbeddingsAsync(paragraphs);

        foreach (var (paragraph, index) in paragraphs.WithIndex())
        {
            command.Parameters.Clear();

            command.CommandText = $"""
                INSERT INTO DocumentChunks (DocumentId, [Index], Content, Embedding)
                VALUES (@DocumentId, @Index, @Content, CAST(@Embedding AS VECTOR({embeddings[index].Length})));
                """;

            command.Parameters.AddWithValue("@DocumentId", documentId);
            command.Parameters.AddWithValue("@Index", index);
            command.Parameters.AddWithValue("@Content", paragraph);
            command.Parameters.AddWithValue("@Embedding", JsonSerializer.Serialize(embeddings[index]));

            await command.ExecuteNonQueryAsync();
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

    public async Task DeleteDocumentAsync(Guid documentId, DbTransaction? transaction = null)
    {
        if (sqlConnection.State == ConnectionState.Closed)
        {
            await sqlConnection.OpenAsync();
        }

        await using var command = sqlConnection.CreateCommand();
        command.Transaction = transaction as SqlTransaction;

        command.CommandText = "DELETE FROM Documents WHERE Id = @DocumentId";
        command.Parameters.AddWithValue("@DocumentId", documentId);

        await command.ExecuteNonQueryAsync();
    }

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

        //await sqlConnection.OpenAsync();
        //await using var command = sqlConnection.CreateCommand();

        //command.CommandText = $"""
        //    SELECT TOP (@MaxRelevantChunks) Content
        //    FROM DocumentChunks
        //    ORDER BY VECTOR_DISTANCE('cosine', Embedding, CAST(@QuestionEmbedding AS VECTOR({questionEmbedding.Length})));
        //    """;

        //command.Parameters.AddWithValue("@MaxRelevantChunks", appSettings.MaxRelevantChunks);
        //command.Parameters.AddWithValue("@QuestionEmbedding", JsonSerializer.Serialize(questionEmbedding));

        //var chunks = new List<string>();

        //await using var reader = await command.ExecuteReaderAsync();
        //while (await reader.ReadAsync())
        //{
        //    var content = reader.GetString(0);
        //    chunks.Add(content);
        //}

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