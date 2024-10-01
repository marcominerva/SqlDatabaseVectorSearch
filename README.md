# SQL Database Vector Search Sample
A repository that showcases the native Vector type in Azure SQL Database to perform embeddings and RAG with Azure OpenAI.

> [!IMPORTANT]  
> Usage of this application requires the Vector support feature in Azure SQL Database, currently in EAP. [See this blog post](https://devblogs.microsoft.com/azure-sql/announcing-eap-native-vector-support-in-azure-sql-database/) for more details.

The application is a Minimal API that exposes endpoints to load documents, generate embeddings and save them into the database as Vectors, and perform searches using Vector Search and RAG. Currently, only PDF files are supported. Embedding and Chat Completion are integrated with [Semantic Kernel](https://github.com/microsoft/semantic-kernel).

![SQL Database Vector Search](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch.png)

### Setup

- [Create an Azure SQL Database](https://learn.microsoft.com/en-us/azure/azure-sql/database/single-database-create-quickstart) on a server that has the Vector Support feature enabled
- Execute the [Scripts.sql](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/Scripts.sql) file to create the tables needed by the application
  - You may need to update the size of the [`VECTOR`](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/Scripts.sql#L17) column to match the size of the embedding model. Currently, the maximum allowed value is 1998.
- Open the [appsettings.json](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/appsettings.json) file and set the connection string to the database and the other settings required by Azure OpenAI
  - If your embedding model supports shortening, like **text-embedding-3-small** and **text-embedding-3-large**, and you want to use this feature, you need to set the [`Dimension`](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/appsettings.json#L17) property to match the value you have used in the SQL script. If your model doesn't provide this feature, or do you want to use the default size, just leave the [`Dimension`](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/appsettings.json#L17) property to NULL. Keep in mind that **text-embedding-3-small** has a dimension of 1536, while **text-embedding-3-large** uses vectors with 3072 elements, so with this latter model it is mandatory to specify a value (that, as said, must be less or equal to 1998).
- Run the application and start importing your PDF documents.