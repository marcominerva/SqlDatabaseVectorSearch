# SQL Database Vector Search Sample
A repository that showcases the native VECTOR type in Azure SQL Database to perform embeddings and RAG with Azure OpenAI.

The application allows you to load documents, generate embeddings, save them into the database as vectors, and perform searches using Vector Search and RAG. Currently, PDF, DOCX, TXT, and MD files are supported. Vectors are saved and retrieved with Entity Framework Core using the [EFCore.SqlServer.VectorSearch](https://github.com/efcore/EfCore.SqlServer.VectorSearch) library. Embedding and Chat Completion are integrated with [Semantic Kernel](https://github.com/microsoft/semantic-kernel).

This repository contains a Blazor Web App as well as a Minimal API that allows you to programmatically interact with embeddings and RAG.

### Web App
![SQL Database Vector Search Web App](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/assets/SqlDatabaseVectorSearch_WebApp.png)

### Web API
![SQL Database Vector Search API](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/assets/SqlDatabaseVectorSearch_API.png)

## Setup

- [Create an Azure SQL Database](https://learn.microsoft.com/en-us/azure/azure-sql/database/single-database-create-quickstart)
- Open the [appsettings.json](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/appsettings.json) file and set the connection string to the database and the other settings required by Azure OpenAI.
  - If your embedding model supports shortening, like **text-embedding-3-small** and **text-embedding-3-large**, and you want to use this feature, you need to set the [`Dimensions`](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/appsettings.json#L17) property to the corresponding value. If your model doesn't provide this feature, or if you want to use the default size, just leave the [`Dimensions`](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/appsettings.json#L17) property as NULL. Keep in mind that **text-embedding-3-small** has a dimension of 1536, while **text-embedding-3-large** uses vectors with 3072 elements, so with this latter model it is mandatory to specify a value (that must be less than or equal to 1998, the maximum currently supported by the VECTOR type).
- You may need to update the size of the [`VECTOR`](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/DataAccessLayer/ApplicationDbContext.cs?plain=1#L42C1-L42C47) column to match the size of the embedding model. The default value is 1536. Currently, the maximum allowed value is 1998. If you change it, remember to also update the [Database Migration](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/DataAccessLayer/Migrations/00000000000000_Initial.cs?plain=1#L35C1-L35C92).
- Run the application and start importing your documents.
- If you want to directly use the APIs:
  - Import your documents with the `/api/documents` endpoint.
  - Ask questions using the `/api/ask` or `/api/ask-streaming` endpoints.

## Supported features

- **Conversation History with Question Reformulation**: This feature allows users to view the history of their conversations, including the ability to reformulate questions for better clarity and understanding. This ensures that users can track their interactions and refine their queries as needed.
- **Information about Token Usage**: Users can access detailed information about token usage, which helps in understanding the consumption of tokens during interactions. This feature provides transparency and helps users manage their token usage effectively.
- **Response Streaming**: This feature enables real-time streaming of responses, allowing users to receive information as it is being processed. This ensures a seamless and efficient flow of information, enhancing the overall user experience.
- **Citations**: The application provides citations for the sources used to justify each answer. This allows users to verify the information and understand the origin of the content provided by the system.

### Example of JSON response

```json
{
  "originalQuestion": "why is mars called the red planet?",
  "reformulatedQuestion": "Why is the planet Mars called the red planet?",
  "answer": "Mars is called the Red Planet because its surface has an orange-red color due to being covered in iron(III) oxide dust, also known as rust. This iron oxide gives Mars its distinctive reddish appearance when observed from Earth and is the origin of its well-known nickname",
  "streamState": "End",
  "tokenUsage": {
    "reformulation": {
      "promptTokens": 812,
      "completionTokens": 11,
      "totalTokens": 823
    },
    "embeddingTokenCount": 10,
    "question": {
      "promptTokens": 31708,
      "completionTokens": 227,
      "totalTokens": 31935
    }
  },
  "citations": [
    {
      "documentId": "b1870ad7-4685-42a3-576a-08ddb01159d5",
      "chunkId": "749aba1e-0db5-4033-cfa6-08ddb0115da3",
      "fileName": "Mars.pdf",
      "quote": "surface of Mars is orange-red because it is covered in iron(III) oxide",
      "pageNumber": 1,
      "indexOnPage": 0
    },
    {
      "documentId": "b1870ad7-4685-42a3-576a-08ddb01159d5",
      "chunkId": "215e7197-513f-4fbe-cfa8-08ddb0115da3",
      "fileName": "Mars.pdf",
      "quote": "Martian surface is caused by ferric oxide, or rust",
      "pageNumber": 3,
      "indexOnPage": 0
    }
  ]
}
```

### How response streaming works

When using the `/api/ask-streaming` endpoint, answers will be streamed as with the typical response from OpenAI. The format of the response is as follows:

```json
[
  {
    "originalQuestion": "why is mars called the red planet?",
    "reformulatedQuestion": "Why is the planet Mars known as the red planet?",
    "answer": null,
    "streamState": "Start",
    "tokenUsage": {
      "reformulation": {
        "promptTokens": 541,
        "completionTokens": 12,
        "totalTokens": 553
      },
      "embeddingTokenCount": 11,
      "question": null
    },
    "citations": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": "Mars",
    "streamState": "Append",
    "tokenUsage": null,
    "citations": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " is",
    "streamState": "Append",
    "tokenUsage": null,
    "citations": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " known",
    "streamState": "Append",
    "tokenUsage": null,
    "citations": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " as",
    "streamState": "Append",
    "tokenUsage": null,
    "citations": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " the",
    "streamState": "Append",
    "tokenUsage": null,
    "citations": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " red",
    "streamState": "Append",
    "tokenUsage": null,
    "citations": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " planet",
    "streamState": "Append",
    "tokenUsage": null,
    "citations": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " because",
    "streamState": "Append",
    "tokenUsage": null,
    "citations": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " its",
    "streamState": "Append",
    "tokenUsage": null,
    "citations": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " surface",
    "streamState": "Append",
    "tokenUsage": null,
    "citations": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " is",
    "streamState": "Append",
    "tokenUsage": null,
    "citations": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " covered",
    "streamState": "Append",
    "tokenUsage": null,
    "citations": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " in",
    "streamState": "Append",
    "tokenUsage": null,
    "citations": null
  },
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": " iron",
    "streamState": "Append",
    "tokenUsage": null,
    "citations": null
  },
  /// ...  
  {
    "originalQuestion": null,
    "reformulatedQuestion": null,
    "answer": null,
    "streamState": "End",
    "tokenUsage": {
      "reformulation": null,
      "embeddingTokenCount": null,
      "question": {
        "promptTokens": 30949,
        "completionTokens": 221,
        "totalTokens": 31170
      }
    },
    "citations": [
      {
        "documentId": "b1870ad7-4685-42a3-576a-08ddb01159d5",
        "chunkId": "749aba1e-0db5-4033-cfa6-08ddb0115da3",
        "fileName": "Mars.pdf",
        "quote": "surface of Mars is orange-red",
        "pageNumber": 1,
        "indexOnPage": 0
      },
      {
        "documentId": "b1870ad7-4685-42a3-576a-08ddb01159d5",
        "chunkId": "215e7197-513f-4fbe-cfa8-08ddb0115da3",
        "fileName": "Mars.pdf",
        "quote": "red-orange appearance of the Martian surface is caused by ferric oxide, or rust",
        "pageNumber": 3,
        "indexOnPage": 0
      }
    ]
  }
]
```
- The first piece of the response has the following characteristics:
  - The *streamState* property is set to `Start`.
  - It contains the question and its reformulation (if not requested, *reformulatedQuestion* will be equal to *originalQuestion*).
  - The *tokenUsage* section holds information about tokens used for reformulation (if done) and for the embedding of the question.
- Then, there are as many elements for the actual answer as necessary:
  - Each one contains a token.
  - The *streamState* property is set to `Append`.
  - *originalQuestion*, *reformulatedQuestion*, and *tokenUsage* are always `null`.
- The stream ends when an element with *streamState* equals `End` is received. This element contains token usage information for the question and the whole answer, and the list of citations.

> [!NOTE]
> If you prefer to use straight SQL, check out the [sql branch](https://github.com/marcominerva/SqlDatabaseVectorSearch/tree/sql).
