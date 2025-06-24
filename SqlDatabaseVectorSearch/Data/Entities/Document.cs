﻿namespace SqlDatabaseVectorSearch.Data.Entities;

public class Document
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset CreationDate { get; set; }

    public virtual ICollection<DocumentChunk> Chunks { get; set; } = [];
}