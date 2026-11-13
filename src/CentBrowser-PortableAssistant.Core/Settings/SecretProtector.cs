using System.Security.Cryptography;
using System.Text;

namespace CentBrowser_PortableAssistant.Core.Settings;

/// <summary>用 Windows DPAPI 加密，只有本机当前账户能解开。</summary>
public static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CentBrowser-PortableAssistant");

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }

    public static string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText)) return "";
        try
        {
            var data = ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // 配置被拷到别的电脑或账户时解不开，按未设置处理，让用户重填
            return "";
        }
    }
}
