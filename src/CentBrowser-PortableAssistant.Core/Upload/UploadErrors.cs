using System.Net.Sockets;
using System.Security.Authentication;
using FluentFTP.Exceptions;

namespace CentBrowser_PortableAssistant.Core.Upload;

public static class UploadErrors
{
    /// <summary>断网、超时、认证失败、配置填错这类可以报给用户、稍后重试的错误。</summary>
    public static bool IsExpected(Exception ex) => ex is
        IOException or
        HttpRequestException or
        SocketException or
        TimeoutException or
        TaskCanceledException or
        AuthenticationException or
        UnauthorizedAccessException or
        FtpException or
        ArgumentException or
        UriFormatException or
        System.Xml.XmlException;

    /// <summary>给界面看的一句话。对得上的用中文，对不上的把原文附在后面，避免只剩一句「连接失败」。</summary>
    public static string Brief(Exception ex)
    {
        string? mapped = null;
        string? chinese = null;
        string? english = null;
        for (var e = ex; e is not null; e = e.InnerException)
        {
            mapped ??= Map(e);
            var line = FirstLine(e.Message);
            if (line.Length == 0) continue;
            if (chinese is null && HasChinese(line)) chinese = line;
            english ??= line;
        }
        if (mapped is not null) return mapped;
        if (chinese is not null) return chinese;
        return english is null ? "连接失败" : "连接失败：" + english;
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\r', '\n')[0].Trim();
        return line.Length <= 80 ? line : line[..80];
    }

    private static string? Map(Exception e) => e switch
    {
        FtpAuthenticationException => "用户名或密码不对",
        FtpInvalidCertificateException => "证书不受信任。自己搭的服务器可以勾选“接受自签名证书”",
        FtpSecurityNotAvailableException => "服务器不支持 FTPS 加密。把加密方式改成“自动”或“不加密”",
        FtpCommandException cmd => cmd.CompletionCode switch
        {
            "530" => "用户名或密码不对",
            "550" or "553" => "没有权限",
            "552" => "空间不足",
            _ => "连不上服务器",
        },
        FtpException => "连不上服务器",
        SocketException => "连不上服务器",
        HttpRequestException http when http.Message.Contains("ASCII", StringComparison.OrdinalIgnoreCase) => "地址里有未编码的特殊字符",
        HttpRequestException http when http.Message.Contains("prematurely", StringComparison.OrdinalIgnoreCase)
            || http.Message.Contains("copying content", StringComparison.OrdinalIgnoreCase) => "传了一半连接断了",
        AuthenticationException => "证书不受信任。自己搭的服务器可以勾选“接受自签名证书”",
        TimeoutException or TaskCanceledException => "连接超时",
        UriFormatException => "地址格式不对",
        DirectoryNotFoundException => "文件夹不存在",
        UnauthorizedAccessException => "没有权限",
        ArgumentException arg when arg.ParamName == "user"
            || arg.Message.Contains("UserName", StringComparison.OrdinalIgnoreCase) => "要填用户名",
        _ => null,
    };

    private static bool HasChinese(string text) => text.Any(c => c is >= '\u4e00' and <= '\u9fff');
}
