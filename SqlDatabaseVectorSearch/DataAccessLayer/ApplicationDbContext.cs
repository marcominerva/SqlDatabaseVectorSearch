using EntityFramework.Exceptions.SqlServer;
using Microsoft.EntityFrameworkCore;
using SqlDatabaseVectorSearch.DataAccessLayer.Entities;

namespace SqlDatabaseVectorSearch.DataAccessLayer;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public virtual DbSet<Document> Documents { get; set; }

    public virtual DbSet<DocumentChunk> DocumentChunks { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseExceptionProcessor();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(entity =>
        {
            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(255);
        });

        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.Property(e => e.Id).HasDefaultValueSql("(newid())");
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Embedding)
                .IsRequired()
                .HasMaxLength(8000)
                .IsVector();

            entity.HasOne(d => d.Document).WithMany(p => p.DocumentChunks)
                .HasForeignKey(d => d.DocumentId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_DocumentChunks_Documents");
        });
    }
}
