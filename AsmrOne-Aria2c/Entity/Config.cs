namespace AsmrOne_Aria2c.Entity;

public class Config
{
    public Proxy? proxy { get; set; }
    public Aria2 aria2 { get; set; } = new();
    

    public string? rootDir { get; set; }
    
    public bool downloadTracks { get; set; } = true;

    public string baseAddress { get; set; } = "https://api.asmr-200.com";
    // 当原始音轨链接源返回错误时,尝试使用另一个域名
    public string fallbackTrackHost { get; set; } = "raw.kiko-play-niptan.one";
    
    public class Proxy
    {
        public string? url { get; set; }
        public bool enable { get; set; } = true;
    }

    public class Aria2
    {
        public string url { get; set; } = "http://localhost:6800/jsonrpc";
        public string password { get; set; } = "";
    }
}