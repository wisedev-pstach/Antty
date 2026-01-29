namespace Antty;

// 1. Knowledge Base Container (Saved to JSON)
public class KnowledgeBase
{
    public KnowledgeBaseMetadata Metadata { get; set; } = new();
    public List<RawChunk> Chunks { get; set; } = new();
}

// 2. Knowledge Base Metadata
public class KnowledgeBaseMetadata
{
    public string Provider { get; set; } = "openai";
    public string ModelName { get; set; } = "text-embedding-3-small";
    public int Dimensions { get; set; } = 512;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// 3. Storage Model (Individual Chunk)
public class RawChunk
{
    public int Id { get; set; }
    public int PageNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
}

// 4. Output Model (Returned to User)
public class RawSearchResult
{
    public string Text { get; set; } = string.Empty;  // The raw paragraph
    public int Page { get; set; }                     // Source page
    public double Score { get; set; }                 // 0.0 to 1.0 confidence
    public string BookSource { get; set; } = string.Empty; // Source document name
}
