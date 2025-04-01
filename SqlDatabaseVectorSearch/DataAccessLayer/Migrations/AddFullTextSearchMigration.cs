using Microsoft.EntityFrameworkCore.Migrations;

namespace SqlDatabaseVectorSearch.DataAccessLayer.Migrations;

public partial class AddFullTextSearchMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Enable full-text search if not already enabled
        migrationBuilder.Sql(@"
            IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'DocumentSearch')
            BEGIN
                CREATE FULLTEXT CATALOG DocumentSearch AS DEFAULT;
            END");

        // Create full-text index on Content column
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 
                FROM sys.fulltext_indexes i
                JOIN sys.tables t ON i.object_id = t.object_id
                WHERE t.name = 'DocumentChunks'
            )
            BEGIN
                CREATE FULLTEXT INDEX ON DocumentChunks(Content)
                KEY INDEX PK_DocumentChunks
                WITH STOPLIST = SYSTEM;
            END");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 
                FROM sys.fulltext_indexes i
                JOIN sys.tables t ON i.object_id = t.object_id
                WHERE t.name = 'DocumentChunks'
            )
            BEGIN
                DROP FULLTEXT INDEX ON DocumentChunks;
            END");

        migrationBuilder.Sql(@"
            IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'DocumentSearch')
            BEGIN
                DROP FULLTEXT CATALOG DocumentSearch;
            END");
    }
} 