namespace SqlDatabaseVectorSearch.Workflows;

public record class EmbeddingRequest(Stream Content, string FileName, string ContentType, Guid? DocumentId)
{
    /// <summary>
    /// Creates an <see cref="EmbeddingRequest"/> from an uploaded <see cref="IFormFile"/>.
    /// </summary>
    /// <param name="file">The uploaded file.</param>
    /// <param name="documentId">The optional identifier of the document to overwrite.</param>
    public static EmbeddingRequest FromFormFile(IFormFile file, Guid? documentId = null) => Create(file.OpenReadStream(), Path.GetFileName(file.FileName), documentId);

    /// <summary>
    /// Creates an <see cref="EmbeddingRequest"/> from a content stream, inferring the content type from the file name.
    /// </summary>
    /// <param name="content">The stream that contains the document content.</param>
    /// <param name="fileName">The name of the document.</param>
    /// <param name="documentId">The optional identifier of the document to overwrite.</param>
    /// <remarks>
    /// The content type is inferred from the file name because the content type declared by the client is not always reliable (for example, for Markdown files).
    /// </remarks>
    public static EmbeddingRequest Create(Stream content, string fileName, Guid? documentId = null)
    {
        var name = Path.GetFileName(fileName);
        return new EmbeddingRequest(content, name, MimeMapping.MimeUtility.GetMimeMapping(name), documentId);
    }
}