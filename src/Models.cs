namespace Antty;

public class KnowledgeBase
{
    public KnowledgeBaseMetadata Metadata { get; set; } = new();
    public List<RawChunk> Chunks { get; set; } = new();
}

public class KnowledgeBaseMetadata
{
    public string Provider { get; set; } = "openai";
    public string ModelName { get; set; } = "text-embedding-3-small";
    public int Dimensions { get; set; } = 512;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RawChunk
{
    public int Id { get; set; }
    public int PageNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
}

public class RawSearchResult
{
    public string Text { get; set; } = string.Empty;
    public int Page { get; set; }
    public double Score { get; set; }
    public string BookSource { get; set; } = string.Empty;
}
