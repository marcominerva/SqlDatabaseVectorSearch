namespace SqlDatabaseVectorSearch.Models;

public record class Question(Guid ConversationId, string Text) : Search(Text);
