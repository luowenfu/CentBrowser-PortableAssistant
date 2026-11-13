using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Core.Upload;

/// <summary>WebDAV 用 PUT、PROPFIND、DELETE。目录必须已经存在。不支持续传，断线后整个文件重传。</summary>
public sealed class WebDavUploader : IUploader
{
    private static readonly HttpMethod Propfind = new("PROPFIND");
    private static readonly XNamespace Dav = "DAV:";

    private readonly HttpClient _http;
    private readonly string _base;
    private readonly string[] _dirSegments;
    private readonly int _uploadKbps;

    public WebDavUploader(UploadTarget target, string password, int uploadKbps = 0)
    {
        _uploadKbps = uploadKbps;
        var address = target.Address.Trim();
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            throw new ArgumentException("地址要以 https:// 开头");

        var handler = new HttpClientHandler();
        if (target.AcceptAnyCertificate) handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{target.UserName}:{password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);

        // 请求地址里的中文要转成百分号编码，否则带中文的目录会传不出去。
        _base = uri.AbsoluteUri.TrimEnd('/');
        _dirSegments = UploaderFactory.NormalizeDir(target.RemoteDir).Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    public Task TestAsync(CancellationToken ct) => UploadProbe.RunAsync(this, ct);

    public async Task<string> UploadAsync(string localPath, string fileName, IProgress<long>? progress, CancellationToken ct)
    {
        await RequireDirAsync(ct);
        var name = await UniqueName.Pick(fileName, async (candidate, token) => await GetSizeAsync(FileUrl(candidate), token) is not null, ct);
        var final = FileUrl(name);
        var size = new FileInfo(localPath).Length;
        try
        {
            await using (var fs = ThrottledStream.Wrap(File.OpenRead(localPath), _uploadKbps))
            using (var content = new StreamContent(new ProgressStream(fs, progress), 1 << 20))
            {
                content.Headers.ContentLength = size;
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var res = await _http.PutAsync(final, content, ct);
                Check(res);
            }

            var remoteSize = await GetSizeAsync(final, ct);
            if (remoteSize is not null && remoteSize != size) throw new IOException("传上去的文件大小不一致");
            return final;
        }
        catch (OperationCanceledException)
        {
            await RemovePartialAsync(final, size, CancellationToken.None);
            throw;
        }
        catch (Exception ex) when (UploadErrors.IsExpected(ex) && !ct.IsCancellationRequested)
        {
            await RemovePartialAsync(final, size, CancellationToken.None);
            throw;
        }
    }

    /// <summary>大小已经对上的文件留着，那是完整的。对不上或看不出大小的半截删掉。</summary>
    private async Task RemovePartialAsync(string url, long expected, CancellationToken ct)
    {
        try
        {
            if (await GetSizeAsync(url, ct) == expected) return;
            await DeleteAsync(url, ct);
        }
        catch (Exception)
        {
            // 清理失败不能盖住原来的上传错误
        }
    }

    public async Task DeleteAsync(string remotePath, CancellationToken ct)
    {
        using var res = await _http.DeleteAsync(remotePath, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return;
        Check(res);
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task RequireDirAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(Propfind, DirUrl() + "/");
        req.Headers.Add("Depth", "0");
        req.Content = new StringContent(
            "<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/></d:prop></d:propfind>",
            Encoding.UTF8, "application/xml");
        using var res = await _http.SendAsync(req, ct);
        if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
            throw new HttpRequestException("远程目录不存在");
        Check(res);
    }

    private async Task<long?> GetSizeAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(Propfind, url);
        req.Headers.Add("Depth", "0");
        req.Content = new StringContent(
            "<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:getcontentlength/></d:prop></d:propfind>",
            Encoding.UTF8, "application/xml");
        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        var xml = XDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var text = xml.Descendants(Dav + "getcontentlength").FirstOrDefault()?.Value;
        return long.TryParse(text, out var n) ? n : null;
    }

    private string DirUrl() => _base + string.Concat(_dirSegments.Select(s => "/" + Uri.EscapeDataString(s)));

    private string FileUrl(string name) => DirUrl() + "/" + Uri.EscapeDataString(name);

    private static void Check(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return;
        throw new HttpRequestException(Explain(res), null, res.StatusCode);
    }

    /// <summary>状态码含义按 RFC 4918（WebDAV）和 RFC 9110（HTTP）。</summary>
    private static string Explain(HttpResponseMessage res)
    {
        if (res.StatusCode == HttpStatusCode.Unauthorized)
        {
            // 服务器只认 NTLM、Digest 这类认证时，Basic 一定不通过，填什么密码都没用
            var schemes = res.Headers.WwwAuthenticate.Select(h => h.Scheme).Distinct().ToList();
            if (schemes.Count > 0 && !schemes.Contains("Basic", StringComparer.OrdinalIgnoreCase))
                return $"服务器要求 {string.Join("/", schemes)} 认证，暂不支持。需要在服务器上开启 Basic 认证";
            return "用户名或密码不对。坚果云等网盘要用“应用密码”";
        }
        return res.StatusCode switch
        {
            HttpStatusCode.Forbidden => "没有权限",
            HttpStatusCode.NotFound => "地址不存在，检查 WebDAV 地址",
            HttpStatusCode.MethodNotAllowed => "这个目录不能放文件，要填共享文件夹",
            HttpStatusCode.Conflict => "远程目录不存在",
            HttpStatusCode.RequestEntityTooLarge => "文件太大",
            HttpStatusCode.InsufficientStorage => "空间不足",
            HttpStatusCode.Locked => "文件被锁定",
            HttpStatusCode.TooManyRequests => "请求太频繁，稍后再试",
            HttpStatusCode.ProxyAuthenticationRequired => "代理服务器要求登录",
            >= HttpStatusCode.InternalServerError => $"服务器出错（{(int)res.StatusCode}）",
            _ => $"连接失败（{(int)res.StatusCode}）",
        };
    }

    /// <summary>地址是 http:// 时，密码和文件都是明文传输。</summary>
    public bool IsPlainHttp => _base.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
}
