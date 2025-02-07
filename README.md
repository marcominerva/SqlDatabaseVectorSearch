# SQL Database Vector Search Sample
A repository that showcases the native VECTOR type in Azure SQL Database to perform embeddings and RAG with Azure OpenAI.

The application is a Minimal API that exposes endpoints to load documents, generate embeddings and save them into the database as Vectors, and perform searches using Vector Search and RAG. Currently, PDF, DOCX and TXT files are supported. Vectors are saved and retrieved with Entity Framework Core using the [EFCore.SqlServer.VectorSearch](https://github.com/efcore/EfCore.SqlServer.VectorSearch) library. Embedding and Chat Completion are integrated with [Semantic Kernel](https://github.com/microsoft/semantic-kernel).

> [!NOTE]
> If you prefer to use straight SQL, check out the [sql branch](https://github.com/marcominerva/SqlDatabaseVectorSearch/tree/sql).

![SQL Database Vector Search](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch.png)

## Setup

- [Create an Azure SQL Database](https://learn.microsoft.com/en-us/azure/azure-sql/database/single-database-create-quickstart) on a server that has the Vector Support feature enabled
- Execute the [Scripts.sql](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/Scripts.sql) file to create the tables needed by the application
  - You may need to update the size of the [`VECTOR`](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/Scripts.sql#L17) column to match the size of the embedding model. Currently, the maximum allowed value is 1998.
- Open the [appsettings.json](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/appsettings.json) file and set the connection string to the database and the other settings required by Azure OpenAI
  - If your embedding model supports shortening, like **text-embedding-3-small** and **text-embedding-3-large**, and you want to use this feature, you need to set the [`Dimensions`](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/appsettings.json#L17) property to match the value you have used in the SQL script. If your model doesn't provide this feature, or do you want to use the default size, just leave the [`Dimensions`](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/appsettings.json#L17) property to NULL. Keep in mind that **text-embedding-3-small** has a dimension of 1536, while **text-embedding-3-large** uses vectors with 3072 elements, so with this latter model it is mandatory to specify a value (that, as said, must be less or equal to 1998).
- Run the application and start importing your documents with `/api/documents` endpoint.
- Ask questions using `/api/ask` or `/api/ask-streaming` endpoints.

## Supported features

- Conversation history with question reformulation
- Information about token usage
- Response streaming

```json
{
  "originalQuestion": "why is mars called the red planet?",
  "reformulatedQuestion": "Why is Mars referred to as the Red Planet?",
  "answer": "Mars is referred to as the Red Planet due to its characteristic reddish color, which is caused by the abundance of iron oxide (rust) on its surface. This distinctive coloration has also been a significant factor in the cultural and mythological associations of Mars across different civilizations.",
  "streamState": null,
  "tokenUsage": {
    "reformulation": {
      "inputTokenCount": 107,
      "outputTokenCount": 10,
      "totalTokenCount": 117
    },
    "embeddingTokenCount": 10,
    "question": {
      "inputTokenCount": 9142,
      "outputTokenCount": 53,
      "totalTokenCount": 9195
    }
  }
}
```

### How response streaming works

When using the `/api/ask-streaming` endpoint, answers will be streamed as happens with the typical response from OpenAI. The format of the response is the following:

```json
[
  {
    "originalQuestion": "why is mars called the red planet?",
    "reformulatedQuestion": "Why is Mars referred to as the Red Planet?",
    "answer": null,
    "streamState": "Start",
    "tokenUsage": {
      "reformulation": {
        "inputTokenCount": 107,
        "outputTokenCount": 10,
        "totalTokenCount": 117
      },
      "embeddingTokenCount": 10,
      "question": null
    }
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": "Mars",
    "streamState": "Append",
    "tokenUsage": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " is",
    "streamState": "Append",
    "tokenUsage": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " called",
    "streamState": "Append",
    "tokenUsage": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " the",
    "streamState": "Append",
    "tokenUsage": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " Red",
    "streamState": "Append",
    "tokenUsage": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " Planet",
    "streamState": "Append",
    "tokenUsage": null
  },
  //...
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": ".",
    "streamState": "Append",
    "tokenUsage": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": null,
    "streamState": "End",
    "tokenUsage": {
      "reformulation": null,
      "embeddingTokenCount": null,
      "question": {
        "inputTokenCount": 8986,
        "outputTokenCount": 31,
        "totalTokenCount": 9017
      }
    }
  }
]
```

- The first piece of the response has the following characteristics:
  - the *streamState* property is set to `Start`,
  - it contains the question and its reformulation (if not requested, *reformulatedQuestion* will be equals to *originalQuestion*)
  - the *tokenUsage* section holds information about token used for reformulation (if done) and for the embedding of the question
- Then, there are as many elements for the actual answer as necessary:
  - each one contains a token
  - The *streamState* property is set to `Append`
  - *origianlQuestion*, *reformulatedQuestion* and *tokenUsage* are always `null`
- The stream ends when an element with *streamState* equals to `End` is received. This element contains token usage information for the question and the whole answer.