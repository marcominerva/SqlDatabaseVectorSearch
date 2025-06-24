# SQL Database Vector Search Sample

[![.NET 9](https://img.shields.io/badge/.NET-9-blue)](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) [![Blazor](https://img.shields.io/badge/Blazor-WebApp-purple)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)

A Blazor Web App and Minimal API for performing RAG (Retrieval Augmented Generation) and vector search using the native VECTOR type in Azure SQL Database and Azure OpenAI.


## Table of Contents
- [Overview](#overview)
- [Screenshots](#screenshots)
- [Prerequisites](#prerequisites)
- [Project Structure](#project-structure)
- [Setup](#setup)
- [Supported Features](#supported-features)
- [How to Use](#how-to-use)
- [Limitations & FAQ](#limitations-faq)
- [Contributing](#contributing)
- [License](#license)

---

## Overview
This application allows you to:
- Load documents (PDF, DOCX, TXT, MD)
- Generate embeddings and save them as vectors in Azure SQL Database
- Perform semantic search and RAG using Azure OpenAI
- Interact via a Blazor Web App or programmatically via Minimal API

Embeddings and chat completion are powered by [Semantic Kernel](https://github.com/microsoft/semantic-kernel). Vectors are managed with [EFCore.SqlServer.VectorSearch](https://github.com/efcore/EfCore.SqlServer.VectorSearch).

## Screenshots

### Web App
![SQL Database Vector Search Web App](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/assets/SqlDatabaseVectorSearch_WebApp.png)

### Web API
![SQL Database Vector Search API](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/assets/SqlDatabaseVectorSearch_API.png)

## Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- [Azure SQL Database](https://learn.microsoft.com/en-us/azure/azure-sql/database/single-database-create-quickstart)
- Azure OpenAI resource and API keys

## Project Structure
- `SqlDatabaseVectorSearch/` - Main Blazor Web App and API
  - `Components/` - Blazor UI components
  - `Data/` - EF Core context, migrations, and entities
  - `Endpoints/` - Minimal API endpoints
  - `Services/` - Business logic and integration services
  - `TextChunkers/` - Text splitting utilities
  - `Settings/` - Configuration classes

## Setup

1. Clone the repository

    ```bash
    git clone https://github.com/marcominerva/SqlDatabaseVectorSearch.git
    ```

2. Configure the database and OpenAI settings
   - Edit `SqlDatabaseVectorSearch/appsettings.json` and set your Azure SQL connection string and OpenAI settings.
   - If using embedding models with shortening (e.g., `text-embedding-3-small` or `text-embedding-3-large`), set the `Dimensions` property accordingly. For `text-embedding-3-large`, you must specify a value <= 1998.
   - If you change the VECTOR size, update both the [ApplicationDbContext](SqlDatabaseVectorSearch/Data/ApplicationDbContext.cs) and the [Initial Migration](SqlDatabaseVectorSearch/Data/Migrations/00000000000000_Initial.cs).

3. Run the application

    ```bash
    dotnet run --project SqlDatabaseVectorSearch/SqlDatabaseVectorSearch.csproj
    ```

5. Access the Web App
   - Navigate to `https://localhost:5001` (or the port shown in the console)

## Supported features

- **Conversation History with Question Reformulation**: This feature allows users to view the history of their conversations, including the ability to reformulate questions for better clarity and understanding. This ensures that users can track their interactions and refine their queries as needed.
- **Information about Token Usage**: Users can access detailed information about token usage, which helps in understanding the consumption of tokens during interactions. This feature provides transparency and helps users manage their token usage effectively.
- **Response Streaming**: This feature enables real-time streaming of responses, allowing users to receive information as it is being processed. This ensures a seamless and efficient flow of information, enhancing the overall user experience.
- **Citations**: The application provides citations for the sources used to justify each answer. This allows users to verify the information and understand the origin of the content provided by the system.

## How to Use

- **Web App**: Use the Blazor interface to upload documents, search, and chat with RAG.
- **API**: Import documents via `POST /api/documents` and ask questions via `POST /api/ask` or `POST /api/ask-streaming`.

#### Example API Request
```
POST /api/ask
Content-Type: application/json

{
    "conversationId": "3d0bd178-499d-433a-b2bc-c35e488d9e2c"
    "text": "Why is Mars called the red planet?"
}
```

#### Example API Response

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
  - *originalQuestion*, *reformulatedQuestion*, *tokenUsage* and *citations* are always `null`.
- The stream ends when an element with *streamState* equals `End` is received. This element contains token usage information for the question and the whole answer, and the list of citations.

## Limitations & FAQ

- **VECTOR column size**: Maximum allowed is 1998. For `text-embedding-3-large`, set `Dimensions` <= 1998.
- **Supported file types**: PDF, DOCX, TXT, MD.
- **Known Issues**: See [Issues](https://github.com/marcominerva/SqlDatabaseVectorSearch/issues)

## Contributing

Contributions are welcome! Please open issues or pull requests. For major changes, discuss them first via an issue.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

---

> [!NOTE]
> If you prefer to use straight SQL, check out the [sql branch](https://github.com/marcominerva/SqlDatabaseVectorSearch/tree/sql).

