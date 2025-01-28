namespace SqlDatabaseVectorSearch.Models;

public record class Response(string Question, string Answer, StreamState? StreamState = null);

public enum StreamState
{
    Start,
    Append,
    End
}