using System.Runtime.InteropServices;

namespace CentBrowser_PortableAssistant.Core.Upload;

/// <summary>Windows 网络连接接口（mpr.dll）。</summary>
internal static class SmbNative
{
    public const int TypeDisk = 1;
    public const int ConnectTemporary = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    public static extern int WNetAddConnection2(ref NetResource netResource, string? password, string? username, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    public static extern int WNetCancelConnection2(string name, int flags, bool force);
}
