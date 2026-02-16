namespace Antty.Models;

public record KnowledgeBase(
    KnowledgeBaseMetadata Metadata,
    List<RawChunk> Chunks
)
{
    public KnowledgeBase() : this(new KnowledgeBaseMetadata(), []) { }
}

public record KnowledgeBaseMetadata
{
    public string Provider { get; init; } = "openai";
    public string ModelName { get; init; } = "text-embedding-3-small";
    public int Dimensions { get; init; } = 512;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}


public record RawChunk(
    int Id,
    int PageNumber,
    string Content,
    float[] Vector
);

public record RawSearchResult(
    string Text,
    int Page,
    double Score,
    string BookSource
);

/// <summary>
/// Tool arguments for searching documents
/// </summary>
public record SearchDocumentsArgs(
    string query = "",
    int maxResults = 5
);

/// <summary>
/// Tool arguments for reading a specific page
/// </summary>
public record ReadPageArgs(
    string documentName = "",
    int pageNumber = 0
);
