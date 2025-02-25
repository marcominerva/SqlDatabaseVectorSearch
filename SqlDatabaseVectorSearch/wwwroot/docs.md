# SQL Database Vector Search

## Setup

- [Create an Azure SQL Database](https://learn.microsoft.com/en-us/azure/azure-sql/database/single-database-create-quickstart)
- Open the [appsettings.json](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/appsettings.json) file and set the connection string to the database and the other settings required by Azure OpenAI
  - If your embedding model supports shortening, like **text-embedding-3-small** and **text-embedding-3-large**, and you want to use this feature, you need to set the [`Dimensions`](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/appsettings.json#L17) property to the corresponding value. If your model doesn't provide this feature, or do you want to use the default size, just leave the [`Dimensions`](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/appsettings.json#L17) property to NULL. Keep in mind that **text-embedding-3-small** has a dimension of 1536, while **text-embedding-3-large** uses vectors with 3072 elements, so with this latter model it is mandatory to specify a value (that, as said, must be less or equal to 1998).
- You may need to update the size of the [`VECTOR`](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/DataAccessLayer/ApplicationDbContext.cs?plain=1#L42C1-L42C47) column to match the size of the embedding model. The default value is 1536. Currently, the maximum allowed value is 1998. If you change it, remember to update also the [Database Migration](https://github.com/marcominerva/SqlDatabaseVectorSearch/blob/master/SqlDatabaseVectorSearch/DataAccessLayer/Migrations/00000000000000_Initial.cs?plain=1#L35C1-L35C92).
- Run the application and start importing your documents

## Supported features

- Conversation history with question reformulation
- Information about token usage
- Response streaming