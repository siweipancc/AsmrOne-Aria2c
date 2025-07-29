using System.Net;
using System.Text.RegularExpressions;
using Aria2NET;
using AsmrOne_Aria2c.Entity;
using log4net;
using log4net.Config;
using Microsoft.VisualBasic.FileIO;
using Microsoft.WindowsAPICodePack.Dialogs;
using Microsoft.WindowsAPICodePack.Shell;
using Newtonsoft.Json;

[assembly: XmlConfigurator(ConfigFile = "log4net.config")]

namespace AsmrOne_Aria2c;

internal partial class Program
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(Program));
    private static HttpClient? _client;
    private static Config? _config;
    private static Aria2NetClient? _aria2;

    private static async Task Main(string[] args)
    {
        var cts = new CancellationTokenSource();

        var config = PrepareConfig();
        // 允许切换根目录
        changeRoot:
        Console.WriteLine("切换根目录请输入任意值");
        var readKey = Console.ReadKey();
        if (ConsoleKey.Enter != readKey.Key)
        {
            config.rootDir = null;
            SetUpRootDir(config);
        }

        // 客户端
        var client = InitClient(config);
        InitClientEnv(client, config);
        // 避免空选项
        SetUpRootDir(config);
        Log.InfoFormat("使用配置: \n{0}", JsonConvert.SerializeObject(config));
        _client = client;
        _config = config;
        _aria2 = new Aria2NetClient(config.aria2.url, config.aria2.password, _client);

        // 检测 aria2c 是否在跑
        detectAria2c:
        try
        {
            await _aria2.GetGlobalOptionAsync(cts.Token);
        }
        catch (Exception e)
        {
            Log.Error("未检测到 aria2c RPC 端点, 请启动 aria2c RPC 服务!!");
            Console.ReadKey();
            goto detectAria2c;
        }

        string? rj;

        inputRj:
        while (true)
        {
            Console.WriteLine("输入RJ号, 输入 1 修改根目录: ");
            rj = Console.ReadLine();
            if (string.IsNullOrEmpty(rj))
            {
                continue;
            }

            if (rj == "1")
            {
                goto changeRoot;
            }

            Log.InfoFormat("使用RJ号: {0}", rj);
            break;
        }

        // 创建预编译的正则表达式
        var match = RjRegex().Match(rj);
        if (match.Success)
        {
            rj = match.Groups["code"].Value;
            Log.InfoFormat("提取到编号: {0}", rj);
        }

        // 获取信息
        WorkInfo? info;
        try
        {
            info = await GetWorkInfo(client, rj, cts.Token);
            if (info == null)
            {
                Console.WriteLine($"非法RJ号: {rj}, 请重新输入");
                goto inputRj;
            }
        }
        catch (Exception e)
        {
            Log.Error($"无法获取作品信息. : {e.Message}", e);
            goto inputRj;
        }

        // 创建主文件夹
        var folderName = FolderName(info);
        var folderPath = FileSystem.CombinePath(config.rootDir!, FolderName(info));

        if (!Directory.Exists(folderName))
        {
            Log.InfoFormat("创建作品文件夹: {0}", folderPath);
            FileSystem.CreateDirectory(folderPath);
        }

        // 写入信息
        var sourceId = info.source_id;
        Log.DebugFormat("info 接口返回详细信息为: \n{0}", JsonConvert.SerializeObject(info));
        var infoPath = FileSystem.CombinePath(folderPath, $"{sourceId}.json");
        await File.WriteAllTextAsync(infoPath, JsonConvert.SerializeObject(info, Formatting.Indented), cts.Token);
        Log.InfoFormat("写入详细信息到: {0}", infoPath);

        // 写入音轨
        List<Track>? tracks;
        try
        {
            tracks = await GetTracks(client, rj, cts.Token);
        }
        catch (Exception e)
        {
            Log.Error($"无法获取音轨信息. : {e.Message}", e);
            goto inputRj;
        }

        if (tracks == null)
        {
            Console.WriteLine($"非法RJ号: {rj}, 请重新输入");
            goto inputRj;
        }

        Log.DebugFormat("track 接口返回详细信息为: \n{0}", JsonConvert.SerializeObject(tracks));
        var trackPath = FileSystem.CombinePath(folderPath, $"{sourceId}.tracks.json");
        await File.WriteAllTextAsync(trackPath, JsonConvert.SerializeObject(tracks, Formatting.Indented), cts.Token);
        Log.InfoFormat("写入音轨详细信息到: {0}", trackPath);

        // 写入图片
        try
        {
            await DownloadMainCoverUnderRoot(info, cts.Token);
        }
        catch (Exception e)
        {
            Log.Error($"无法获取下载主视图. : {e.Message}", e);
            goto inputRj;
        }

        // 将文件丢给 aria2 下载
        if (config.downloadTracks)
        {
            try
            {
                await DownloadTracks(info, tracks, cts.Token);
            }
            catch (Exception e)
            {
                Log.Error($"无法下载音轨. : {e.Message}", e);
            }
        }
        else
        {
            Log.Warn("跳过下载音轨");
        }

        goto inputRj;
        // ReSharper disable once FunctionNeverReturns
    }


    private static string SanitizeFileName(string input, char replacement = '_')
    {
        List<char> extraInvalidChars = [];
        var invalidChars = Path.GetInvalidFileNameChars().ToList();
        invalidChars.AddRange(extraInvalidChars);
        return string.Concat(input.Select(c => invalidChars.Contains(c) ? replacement : c));
    }

    private static async Task DownloadMainCoverUnderRoot(WorkInfo info, CancellationToken ct)
    {
        var config = _config!;
        var client = _client!;
        var folderName = FolderName(info);

        var cMatch = CoverRegex().Match(info.mainCoverUrl);
        string extension;
        if (cMatch.Success)
        {
            extension = cMatch.Groups["extension"].Value;
            Log.InfoFormat("提取到主视图扩展名: {0}", extension);
        }
        else
        {
            extension = "png";
        }

        var imgName = folderName + "." + extension;
        var savePath = FileSystem.CombinePath(config.rootDir!, imgName);
        // 检测大小,是否需要重新下载
        var headRes = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, info.mainCoverUrl), ct);
        headRes.EnsureSuccessStatusCode();
        var fileInfo = FileSystem.GetFileInfo(savePath);
        if (fileInfo.Exists && fileInfo.Length == headRes.Content.Headers.ContentLength)
        {
            Log.InfoFormat("跳过已下载完整的主视图文件 {0}, 大小 {1}", info.mainCoverUrl, fileInfo.Length);
            return;
        }

        using var response = await client.GetAsync(info.mainCoverUrl, ct);
        response.EnsureSuccessStatusCode();
        // 读取内容为字节数组
        var fileBytes = await response.Content.ReadAsByteArrayAsync(ct);
        // 将字节数组写入文件
        await File.WriteAllBytesAsync(savePath, fileBytes, ct);
        Log.InfoFormat("写入主视图到: {0}", savePath);
    }

    private static string FolderName(WorkInfo info)
    {
        var vas = string.Join(", ", info.vas.Select(s => s.name).ToList());
        var folderName = $"{info.source_id} {info.title} 【{vas}】";
        var sanitizeFileName = SanitizeFileName(folderName);
        if (sanitizeFileName != folderName)
        {
            Log.InfoFormat("检测到非法平台字符, 使用新的名字: {0}, \n旧名字: {1}", sanitizeFileName, folderName);
        }

        return sanitizeFileName;
    }

    private static async Task DownloadTracks(WorkInfo info, List<Track> tracks, CancellationToken ctt)
    {
        var config = _config!;
        var rootDir = config.rootDir!;
        var folderName = FolderName(info);
        var folderPath = Path.Combine(rootDir, folderName);
        if (!FileSystem.DirectoryExists(folderPath))
        {
            FileSystem.CreateDirectory(folderPath);
            Log.InfoFormat("已创建作品目录: {0}", folderPath);
        }

        foreach (var track in tracks)
        {
            var stack = new Stack<(Track node, string parentPath)>();
            stack.Push((track, folderPath));
            while (stack.Count > 0)
            {
                var (node, parentPath) = stack.Pop();
                var title = node.Title;
                Log.InfoFormat("处理文件: {0}, 类别: {1}", title, node.Type);
                var curPath = Path.Combine(parentPath, title);
                if (node.Type == "folder")
                {
                    if (!FileSystem.DirectoryExists(curPath))
                    {
                        Log.InfoFormat("创建文件夹: {0}, 完整路径为: {1}", title, curPath);
                        FileSystem.CreateDirectory(curPath);
                    }
                    else
                    {
                        Log.InfoFormat("文件夹已存在: {0}, 完整路径为: {1}", title, curPath);
                    }

                    // 存在子类别
                    if (node.Children == null)
                    {
                        Log.InfoFormat("当前文件夹已无子元素");
                        continue;
                    }

                    var children = node.Children.ToList();
                    children.Reverse();
                    children.ForEach(f => stack.Push((f, curPath)));
                }
                else
                {
                    var fullFilePath = Path.Join(parentPath, node.Title);
                    var fileInfo = FileSystem.GetFileInfo(fullFilePath);
                    if (fileInfo.Exists && fileInfo.Length == node.Size)
                    {
                        Log.InfoFormat("文件已经存在: {0}", fullFilePath);
                        continue;
                    }

                    var aria2Cache = $"{fullFilePath}.aria2";
                    if (FileSystem.FileExists(aria2Cache))
                    {
                        Log.WarnFormat("存在 Aria2 下载中间文件: {0}", aria2Cache);
                        continue;
                    }

                    // 修复一些存在问题的链接
                    var mediaDownloadUrl = await PrepareMediaDownloadUrl(node.MediaDownloadUrl, node.Size, ctt);
                    var response = await _aria2!.AddUriAsync([mediaDownloadUrl],
                        new Dictionary<string, object>
                        {
                            { "dir", parentPath },
                            { "out", node.Title }
                        }, 0, ctt);
                    Log.InfoFormat("文件即将下载到: {0}", fullFilePath);
                    Log.InfoFormat("Aria2 返回 {0}", response);
                }
            }
        }
    }

    private static async Task<string> PrepareMediaDownloadUrl(string mediaDownloadUrl, long size,
        CancellationToken ctt)
    {
        Log.DebugFormat("检查音轨源链接是否可用: {0}, 大小: {1}", mediaDownloadUrl, size);
        var response = await _client!.SendAsync(new HttpRequestMessage(HttpMethod.Head, mediaDownloadUrl), ctt);
        try
        {
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength == size)
            {
                Log.DebugFormat("原链接可用");
                return mediaDownloadUrl;
            }
        }
        catch (Exception e)
        {
            Log.Error($"原始音轨链接返回错误: {e.Message}", e);
        }

        var uri = new Uri(mediaDownloadUrl);
        var builder = new UriBuilder(uri)
        {
            Host = _config!.fallbackTrackHost
        };
        var fallbackDownloadUrl = builder.ToString();
        response = await _client!.SendAsync(new HttpRequestMessage(HttpMethod.Head, fallbackDownloadUrl), ctt);
        try
        {
            response.EnsureSuccessStatusCode();
            Log.InfoFormat("使用备用的音轨源链接: {0}", fallbackDownloadUrl);
            return fallbackDownloadUrl;
        }
        catch (Exception e)
        {
            Log.Error($"备用音轨链接返回错误: {e.Message}", e);
            return mediaDownloadUrl;
        }
    }

    private static async Task<List<Track>?> GetTracks(HttpClient client, string rj, CancellationToken ctsToken)
    {
        var tempPath = Path.GetTempPath();
        var cachePath = FileSystem.CombinePath(tempPath, "asmrOne/workTracks");
        if (!FileSystem.DirectoryExists(cachePath))
        {
            Log.InfoFormat("创建缓存目录: {0}", cachePath);
            FileSystem.CreateDirectory(cachePath);
        }

        var fileName = $"{rj}.tracks.json";
        var filePath = FileSystem.CombinePath(cachePath, fileName);
        if (FileSystem.FileExists(filePath))
        {
            return JsonConvert.DeserializeObject<List<Track>>(FileSystem.ReadAllText(filePath));
        }

        var result = await client.GetAsync($"/api/tracks/{rj}?v=2", ctsToken);
        result.EnsureSuccessStatusCode();
        var json = await result.Content.ReadAsStringAsync(ctsToken);
        List<Track>? obj;
        try
        {
            obj = JsonConvert.DeserializeObject<List<Track>>(json);
        }
        catch (Exception e)
        {
            Log.Error("非法音轨信息返回", e);
            obj = null;
        }

        if (obj != null)
        {
            FileSystem.WriteAllText(filePath, JsonConvert.SerializeObject(obj, Formatting.Indented), false);
        }
        else
        {
            return null;
        }

        return JsonConvert.DeserializeObject<List<Track>>(FileSystem.ReadAllText(filePath));
    }

    private static async Task<WorkInfo?> GetWorkInfo(HttpClient client, string rj, CancellationToken ctsToken)
    {
        var tempPath = Path.GetTempPath();
        var cachePath = FileSystem.CombinePath(tempPath, "asmrOne/workInfo");
        if (!FileSystem.DirectoryExists(cachePath))
        {
            Log.InfoFormat("创建缓存目录: {0}", cachePath);
            FileSystem.CreateDirectory(cachePath);
        }

        var fileName = $"{rj}.json";
        var filePath = FileSystem.CombinePath(cachePath, fileName);
        if (FileSystem.FileExists(filePath))
        {
            return JsonConvert.DeserializeObject<WorkInfo>(FileSystem.ReadAllText(filePath));
        }

        var result = await client.GetAsync($"/api/workInfo/{rj}", ctsToken);
        result.EnsureSuccessStatusCode();
        var json = await result.Content.ReadAsStringAsync(ctsToken);
        WorkInfo? obj;
        try
        {
            obj = JsonConvert.DeserializeObject<WorkInfo>(json);
        }
        catch (Exception e)
        {
            Log.Error("非法返回", e);
            obj = null;
        }

        if (!string.IsNullOrEmpty(obj?.title))
        {
            FileSystem.WriteAllText(filePath, JsonConvert.SerializeObject(obj, Formatting.Indented), false);
        }
        else
        {
            return null;
        }

        return JsonConvert.DeserializeObject<WorkInfo>(FileSystem.ReadAllText(filePath));
    }

    private static void SetUpRootDir(Config config)
    {
        // 设定根目录
        while (true)
        {
            if (string.IsNullOrEmpty(config.rootDir))
            {
                var dlg = new CommonOpenFileDialog
                {
                    IsFolderPicker = true,
                    Title = "请选择根保存目录"
                };

                if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    config.rootDir = dlg.FileName;
                }
                else
                {
                    continue;
                }

                PersistConfig(config);
            }
            else
            {
                break;
            }
        }
    }

    private static HttpClient InitClient(Config config)
    {
        // 客户端
        HttpClient client;
        if ((config.proxy?.enable ?? false) && !string.IsNullOrEmpty(config.proxy.url))
        {
            var proxy = new WebProxy
            {
                Address = new Uri(config.proxy.url),
                BypassProxyOnLocal = true,
                UseDefaultCredentials = false,
            };
            var httpClientHandler = new HttpClientHandler
            {
                Proxy = proxy
            };
            httpClientHandler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            client = new HttpClient(handler: httpClientHandler, disposeHandler: true);
        }
        else
        {
            client = new HttpClient();
        }

        return client;
    }

    private static void InitClientEnv(HttpClient client, Config config)
    {
        // 基础 api 接口
        client.BaseAddress = new Uri(config.baseAddress);
    }

    private static void PersistConfig(Config updatedConfig)
    {
        using var writer = new StreamWriter(Directory.GetCurrentDirectory() + @"/config.json");
        writer.Write(JsonConvert.SerializeObject(updatedConfig, Formatting.Indented));
    }

    private static Config PrepareConfig()
    {
        using var r = new StreamReader(Directory.GetCurrentDirectory() + @"/config.json");
        var readToEnd = r.ReadToEnd();
        r.Close();
        var config = JsonConvert.DeserializeObject<Config>(readToEnd);
        if (string.IsNullOrEmpty(config!.rootDir))
        {
            var downloads = KnownFolders.Downloads.Path;
            // var downloads = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            config.rootDir = downloads;
            PersistConfig(config);
        }

        Log.InfoFormat("使用配置: \n{0}", JsonConvert.SerializeObject(config, Formatting.Indented));
        return config;
    }

    [GeneratedRegex(@"(?:https:\/\/asmr\.one\/work\/)?[^\d]{0,}(?<code>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, "zh-CN")]
    private static partial Regex RjRegex();

    [GeneratedRegex(@"\/(?<imgName>\d+\.(?<extension>jpg|png))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, "zh-CN")]
    private static partial Regex CoverRegex();
}