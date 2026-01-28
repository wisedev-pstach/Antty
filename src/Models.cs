namespace Antty;

// 1. Storage Model (Saved to JSON)
public class RawChunk
{
    public int Id { get; set; }
    public int PageNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
}

// 2. Output Model (Returned to User)
public class RawSearchResult
{
    public string Text { get; set; } = string.Empty;  // The raw paragraph
    public int Page { get; set; }                     // Source page
    public double Score { get; set; }                 // 0.0 to 1.0 confidence
}
