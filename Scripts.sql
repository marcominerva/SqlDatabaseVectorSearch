CREATE TABLE [dbo].[DocumentChunks2](
	[Id] [uniqueidentifier] NOT NULL,
	[DocumentId] [uniqueidentifier] NOT NULL,
	[Index] [int] NOT NULL,
	[Content] [nvarchar](max) NOT NULL,
	[Embedding] [vector](1536) NOT NULL,
 CONSTRAINT [PK_DocumentChunks2] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)) 
GO

CREATE TABLE [dbo].[Documents2](
	[Id] [uniqueidentifier] NOT NULL,
	[Name] [nvarchar](255) NOT NULL,
	[CreationDate] [datetimeoffset](7) NOT NULL,
 CONSTRAINT [PK_Documents2] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
))
GO

ALTER TABLE [dbo].[DocumentChunks2]  WITH CHECK ADD  CONSTRAINT [FK_DocumentChunks2_Documents2] FOREIGN KEY([DocumentId])
REFERENCES [dbo].[Documents2] ([Id])
ON DELETE CASCADE
GO

ALTER TABLE [dbo].[Documents2] ADD  CONSTRAINT [DF_Documents2_Id]  DEFAULT (newsequentialid()) FOR [Id]
GO

ALTER TABLE [dbo].[DocumentChunks2] ADD  CONSTRAINT [DF_DocumentChunks2_Id]  DEFAULT (newsequentialid()) FOR [Id]
GO