using System.Data;
using Microsoft.Agents.AI;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using SqlDatabaseVectorSearch.Data;
using SqlDatabaseVectorSearch.Settings;

namespace SqlDatabaseVectorSearch.Services;

public class DocumentContextProviderService(ApplicationDbContext dbContext, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, IOptions<AppSettings> appSettingsOptions)
{
    private readonly AppSettings appSettings = appSettingsOptions.Value;

    public async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        // Perform Vector Search on SQL Database.
        var questionEmbedding = await embeddingGenerator.GenerateVectorAsync(query, cancellationToken: cancellationToken);
        var embeddingVector = new SqlVector<float>(questionEmbedding);

        var chunks = await dbContext.DocumentChunks.Include(c => c.Document)
                    .OrderBy(c => EF.Functions.VectorDistance("cosine", c.Embedding, embeddingVector))
                    .Take(appSettings.MaxRelevantChunks).Select(c => new TextSearchProvider.TextSearchResult
                    {
                        SourceLink = c.Id.ToString().ToLowerInvariant(),
                        SourceName = c.Document.Name,
                        Text = c.Content,
                        RawRepresentation = c.PageNumber
                    })
                    .ToListAsync(cancellationToken);

        return chunks;
    }
}