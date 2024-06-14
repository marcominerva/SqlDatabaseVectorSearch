namespace SqlDatabaseVectorSearch.Models;

public record Question(Guid ConversationId, string Text) : Search(Text);
