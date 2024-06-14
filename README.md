# SQL Database Vector Search Sample
A repository that showcases the Vector Support in Azure SQL Database to perform embeddings and RAG with Azure OpenAI.

> [!IMPORTANT]  
> Usage of this application requires the Vector Support feature in Azure SQL Database, currently in EAP. [See this blog post](https://devblogs.microsoft.com/azure-sql/announcing-eap-native-vector-support-in-azure-sql-database/) for more details.

The application is a Minimal API that exposes endpoints to load documents, generate embeddings and save them into the database as Vectors, and perform searches using Vector Search and RAG. Currently, only PDF files are supported. Database access is done using the [EFCore.SqlServer.VectorSearch](https://github.com/efcore/EfCore.SqlServer.VectorSearch) library.

![SqlServerVectorSearch](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlServerVectorSearch.png)

### Setup

- [Create an Azure SQL Database](https://learn.microsoft.com/en-us/azure/azure-sql/database/single-database-create-quickstart) on a server that has the Vector Support feature enabled.
- Execute the [Scripts.sql](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/Scripts.sql) file to create the tables needed by the application.
- Open the [appsettings.json](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/appsettings.json) file and set the connection string to the database and the other settings required by Azure OpenAI.
- Run the application and start importing your PDF document.
