# SQL Database Vector Search Sample

[![.NET 10](https://img.shields.io/badge/.NET-10-blue)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
[![Minimal API](https://img.shields.io/badge/Minimal%20API-Available-green)](https://dotnet.microsoft.com/apps/aspnet/apis)
[![Blazor](https://img.shields.io/badge/Blazor-WebApp-purple)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)

A Blazor Web App and Minimal API for performing RAG (Retrieval Augmented Generation) and vector search using the native VECTOR type in Azure SQL Database or SQL Server 2025, Azure OpenAI, and [Microsoft Agent Framework](https://github.com/microsoft/agent-framework).

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
- Generate embeddings and save them as vectors in Azure SQL Database or SQL Server 2025
- Perform semantic search and RAG using Azure OpenAI and Microsoft Agent Framework agents
- Interact via a Blazor Web App or programmatically via Minimal API

The native `VECTOR` type is available in both Azure SQL Database and SQL Server 2025, so no external vector store is required.

Embeddings and chat completion are orchestrated with [Microsoft Agent Framework](https://github.com/microsoft/agent-framework). The application uses an embedding workflow to import documents, a reformulation agent to rewrite follow-up questions with conversation context, and a RAG agent connected to a SQL vector-search context provider.

## Screenshots

### Web App
![SQL Database Vector Search Web App](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/assets/SqlDatabaseVectorSearch_WebApp.png)

### Web API
![SQL Database Vector Search API](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/assets/SqlDatabaseVectorSearch_API.png)

## Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- One of the following, both of which support the native `VECTOR` type:
  - [Azure SQL Database](https://learn.microsoft.com/en-us/azure/azure-sql/database/single-database-create-quickstart)
  - [SQL Server 2025](https://learn.microsoft.com/en-us/sql/relational-databases/vectors/vectors-sql-server) or later
- Azure OpenAI resource and API keys

## Project Structure
- `SqlDatabaseVectorSearch/` - Main Blazor Web App and API
  - `Components/` - Blazor UI components
  - `ContentDecoders/` - Decoders that extract text from PDF, DOCX, TXT and MD files
  - `Data/` - EF Core context, migrations, and entities
  - `Endpoints/` - Minimal API endpoints
  - `Extensions/` - Extension methods and helpers
  - `Models/` - Request and response models
  - `Services/` - Business logic and integration services
  - `Settings/` - Configuration classes
  - `TextChunkers/` - Text splitting utilities
  - `Validations/` - Request validators
  - `Workflows/` - Microsoft Agent Framework workflow executors for document import and embedding generation

## Setup

1. Clone the repository

    ```bash
    git clone https://github.com/marcominerva/SqlDatabaseVectorSearch.git
    ```

2. Configure the database and OpenAI settings
   - Edit `SqlDatabaseVectorSearch/appsettings.json` and set your connection string (Azure SQL Database or SQL Server 2025) and OpenAI settings.
   - **Important**: The `ModelId` values for both `ChatCompletion` and `Embedding` are used only for token counting via `Microsoft.ML.Tokenizers`, while the actual calls to the service use the `Deployment` values. `ModelId` must therefore be a model name recognized by the tokenizer library (e.g., `gpt-5`, `gpt-4.1`, `gpt-4o`, `gpt-4`, `gpt-3.5-turbo`, `text-embedding-3-small`, `text-embedding-3-large`, `text-embedding-ada-002`), which is typically different from the deployment name you have chosen in Azure OpenAI. If a model is not recognized, `TiktokenTokenizer.CreateForModel` throws at startup: in that case, fall back to the closest supported model that shares the same encoding (for example `gpt-4o` for newer GPT models).
   - If using embedding models with shortening (e.g., `text-embedding-3-small` or `text-embedding-3-large`), set the `Dimensions` property accordingly. For `text-embedding-3-large`, you must specify a value <= 1998.
   - If you change the VECTOR size, update both the [ApplicationDbContext](SqlDatabaseVectorSearch/Data/ApplicationDbContext.cs) and the [Initial Migration](SqlDatabaseVectorSearch/Data/Migrations/00000000000000_Initial.cs).

3. Run the application

    ```bash
    dotnet run --project SqlDatabaseVectorSearch/SqlDatabaseVectorSearch.csproj
    ```

4. Access the Web App
   - Navigate to `https://localhost:7025` (or the port shown in the console)

## Supported features

- **Microsoft Agent Framework orchestration**: Document import is implemented as a workflow, while question reformulation and RAG are implemented as agents.
- **Conversation history with question reformulation**: The reformulation agent rewrites each question using the conversation context before vector search is performed.
- **SQL vector-search context provider**: The RAG agent receives relevant chunks from Azure SQL Database or SQL Server 2025 through a `TextSearchProvider` backed by native VECTOR search.
- **Information about token usage**: The Blazor chat page and API responses expose token usage for reformulation and final answer generation.
- **Response streaming**: The Blazor chat page and the `/api/ask-streaming` endpoint stream answers, appending tokens as they arrive.
- **Markdown source citations**: Citations are included directly in the generated Markdown answer as a localized sources section with source name, page number when available, and a short supporting excerpt.

## How to Use

- **Web App**: Use the Blazor interface to manage documents and chat with your indexed content. The chat page streams answers, shows token usage, supports conversation reset, and renders source citations as part of the Markdown answer.
- **API**: Import documents via `POST /api/documents` and ask questions via `POST /api/ask` or `POST /api/ask-streaming`.

### How it works

1. Documents are uploaded through the API and processed by the `EmbeddingWorkflow`.
2. The workflow converts the uploaded file into text, chunks it, generates embeddings, and stores documents, chunks, and VECTOR embeddings in Azure SQL Database or SQL Server 2025.
3. When a question is asked, the `ReformulationAgent` can rewrite it using the current conversation context.
4. The `RagAgent` receives relevant SQL vector-search results through a `TextSearchProvider` and answers using only the provided context.
5. Sources are not returned as a separate JSON collection. They are formatted directly in the Markdown answer.

#### Example API Request
```
POST /api/ask
Content-Type: application/json

{
    "conversationId": "3d0bd178-499d-433a-b2bc-c35e488d9e2c",
    "text": "Why is Mars called the red planet?"
}
```

#### Example API Response

```json
{
  "conversationId": "3d0bd178-499d-433a-b2bc-c35e488d9e2c",
  "originalQuestion": "why is mars called the red planet?",
  "reformulatedQuestion": "Why is the planet Mars called the red planet?",
  "answer": "Mars is called the Red Planet because its surface has an orange-red color caused by iron oxide dust.\n\n*Sources*\n1. **Mars.pdf**, page 1: *surface of Mars is orange-red because it is covered in iron oxide dust*",
  "streamState": null,
  "tokenUsage": {
    "reformulation": {
      "inputTokenCount": 812,
      "outputTokenCount": 11,
      "totalTokenCount": 823
    },
    "question": {
      "inputTokenCount": 31708,
      "outputTokenCount": 227,
      "totalTokenCount": 31935
    }
  }
}
```

### How response streaming works

When using the `/api/ask-streaming` endpoint, answers are streamed as [Server-Sent Events](https://developer.mozilla.org/docs/Web/API/Server-sent_events). Each event has a name matching the `streamState` value and a JSON `Response` payload in the `data` field. The format is as follows:

```text
event: Start
data: {"conversationId":"3d0bd178-499d-433a-b2bc-c35e488d9e2c","originalQuestion":"why is mars called the red planet?","reformulatedQuestion":"Why is the planet Mars known as the red planet?","answer":null,"streamState":"Start","tokenUsage":{"reformulation":{"inputTokenCount":541,"outputTokenCount":12,"totalTokenCount":553,"cachedInputTokenCount":0,"reasoningTokenCount":0,"inputAudioTokenCount":null,"inputTextTokenCount":null,"outputAudioTokenCount":null,"outputTextTokenCount":null,"additionalCounts":null},"question":null}}

event: Delta
data: {"conversationId":"3d0bd178-499d-433a-b2bc-c35e488d9e2c","originalQuestion":null,"reformulatedQuestion":null,"answer":"Mars","streamState":"Delta","tokenUsage":null}

event: Delta
data: {"conversationId":"3d0bd178-499d-433a-b2bc-c35e488d9e2c","originalQuestion":null,"reformulatedQuestion":null,"answer":" is known as the red planet because its surface is rich in iron oxide dust.\n\n","streamState":"Delta","tokenUsage":null}

event: Delta
data: {"conversationId":"3d0bd178-499d-433a-b2bc-c35e488d9e2c","originalQuestion":null,"reformulatedQuestion":null,"answer":"Sources\n1. **Mars.pdf**, page 1: *surface of Mars is orange-red because it is covered in iron oxide dust*","streamState":"Delta","tokenUsage":null}

event: End
data: {"conversationId":"3d0bd178-499d-433a-b2bc-c35e488d9e2c","originalQuestion":null,"reformulatedQuestion":null,"answer":null,"streamState":"End","tokenUsage":{"reformulation":null,"question":{"inputTokenCount":30949,"outputTokenCount":221,"totalTokenCount":31170,"cachedInputTokenCount":3840,"reasoningTokenCount":0,"inputAudioTokenCount":null,"inputTextTokenCount":null,"outputAudioTokenCount":null,"outputTextTokenCount":null,"additionalCounts":null}}}
```

- The first event has the following characteristics:
  - The SSE event name is `Start`.
  - The *streamState* property is set to `Start`.
  - It contains the question and its reformulation (if not requested, *reformulatedQuestion* will be equal to *originalQuestion*).
  - The *tokenUsage* section holds information about tokens used for reformulation, if done.
- Then, there are as many `Delta` events as necessary for the actual answer:
  - Each event contains a token or chunk of generated text in the *answer* property.
  - The *streamState* property is set to `Delta`.
  - *originalQuestion*, *reformulatedQuestion* and *tokenUsage* are always `null`.
- The stream ends when an `End` event is received. This event contains token usage information for the final answer.
- Sources are included in the Markdown answer text.

## Limitations & FAQ

- **Database**: Azure SQL Database or SQL Server 2025 (or later). Both provide the native `VECTOR` type used by this sample.
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

