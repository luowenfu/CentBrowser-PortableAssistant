using System.Net;
using System.Net.Http;

namespace CentBrowser_PortableAssistant.Core.Update;

internal static class UpdateHttp
{
    private static readonly HttpClient Api = Create(decompress: true);
    private static readonly HttpClient File = Create(decompress: false);

    public static Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken ct) =>
        SendAsync(Api, request, timeout, ct);

    public static Task<HttpResponseMessage> SendFileAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken ct) =>
        SendAsync(File, request, timeout, ct);

    private static HttpClient Create(bool decompress)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = decompress ? DecompressionMethods.All : DecompressionMethods.None,
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CentBrowser-PortableAssistant");
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, TimeSpan timeout, CancellationToken ct)
    {
        for (var hop = 0; hop < 10; hop++)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(timeout);
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                request.Dispose();
                throw new HttpRequestException("连接超时");
            }
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var next = response.Headers.Location;
            response.Dispose();
            request.Dispose();
            if (next is null) throw new HttpRequestException("跳转没有地址");
            if (!next.IsAbsoluteUri) throw new HttpRequestException("跳转地址不完整");
            if (next.Scheme != Uri.UriSchemeHttps) throw new HttpRequestException("跳转不是 https");
            request = new HttpRequestMessage(HttpMethod.Get, next);
        }
        request.Dispose();
        throw new HttpRequestException("跳转次数过多");
    }
}
