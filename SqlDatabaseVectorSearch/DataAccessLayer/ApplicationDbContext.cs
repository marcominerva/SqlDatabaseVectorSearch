using EntityFramework.Exceptions.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.SqlServer;
using SqlDatabaseVectorSearch.DataAccessLayer.Entities;

namespace SqlDatabaseVectorSearch.DataAccessLayer;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public virtual DbSet<Document> Documents { get; set; }

    public virtual DbSet<DocumentChunk> DocumentChunks { get; set; }

    public async Task EnsureFullTextSearchAsync()
    {
        await Database.ExecuteSqlRawAsync(@"
            IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'DocumentSearch')
            BEGIN
                CREATE FULLTEXT CATALOG DocumentSearch AS DEFAULT;
            END");

        await Database.ExecuteSqlRawAsync(@"
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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        optionsBuilder.UseExceptionProcessor();
        //optionsBuilder.EnableSensitiveDataLogging();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("Documents");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(255);
        });

        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.ToTable("DocumentChunks");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Embedding)
                .HasColumnType("vector(1536)")
                .IsRequired();

            entity.HasOne(d => d.Document).WithMany(p => p.Chunks)
                .HasForeignKey(d => d.DocumentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_DocumentChunks_Documents");
        });
    }
}