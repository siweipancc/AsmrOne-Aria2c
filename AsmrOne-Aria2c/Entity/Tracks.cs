using Newtonsoft.Json;

namespace AsmrOne_Aria2c.Entity;

public class Track
{
    public string Type { get; set; }
    public string Title { get; set; }
    public List<Track>? Children { get; set; }
    public string Hash { get; set; }
    [JsonProperty("Work")] public Work work { get; set; }
    public string WorkTitle { get; set; }
    public string MediaStreamUrl { get; set; }
    public string MediaDownloadUrl { get; set; }
    public double Duration { get; set; }
    public long Size { get; set; }

    public class Work
    {
        public int Id { get; set; }
        public string? SourceId { get; set; }
        public string? SourceType { get; set; }
    }
}