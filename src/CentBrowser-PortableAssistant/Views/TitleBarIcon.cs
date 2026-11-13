using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CentBrowser_PortableAssistant.Views;

/// <summary>去掉标题栏左上角的小图标，标题文字顶到最左边，不留空位。</summary>
public static class TitleBarIcon
{
    public static void Remove(Window window) =>
        window.SourceInitialized += (_, _) => Apply(new WindowInteropHelper(window).Handle);

    private static void Apply(IntPtr hwnd)
    {
        // 对话框边框的窗口没有图标时，标题栏不画图标也不留位置
        SetWindowLongPtr(hwnd, GwlExStyle, GetWindowLongPtr(hwnd, GwlExStyle) | WsExDlgModalFrame);
        SendMessage(hwnd, WmSetIcon, IconSmall, IntPtr.Zero);
        SendMessage(hwnd, WmSetIcon, IconBig, IntPtr.Zero);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
    }

    private const int GwlExStyle = -20;
    private const nint WsExDlgModalFrame = 0x1;
    private const uint WmSetIcon = 0x80;
    private const nint IconSmall = 0, IconBig = 1;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpFrameChanged = 0x20;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(IntPtr hwnd, int index, nint value);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, nint wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
