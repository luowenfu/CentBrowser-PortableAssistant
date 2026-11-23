using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CentBrowser_PortableAssistant.Views;

namespace CentBrowser_PortableAssistant;

public partial class MainWindow : Window
{
    private TargetsPage? _targets;
    private QueuePage? _queue;
    private AboutPage? _about;
    private bool _holdClose;

    public MainWindow()
    {
        InitializeComponent();
        var raw = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var version = raw.Split('+')[0];
        Title = string.IsNullOrEmpty(version) ? "Cent Browser 便携助手" : $"Cent Browser 便携助手 v{version}";
        // 系统缩放大于 100% 时逻辑工作区会变小，默认尺寸放不下就缩到屏幕以内
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - 40);
        Height = Math.Min(Height, area.Height);
        MaxHeight = area.Height;
        MinWidth = Math.Min(MinWidth, Width);
        MinHeight = Math.Min(MinHeight, Height);
        Bar.QueueRequested += OpenQueue;
        Plans.TargetsRequested += OpenTargets;
        App.Updates.Changed += SyncUpdateButton;
        SyncUpdateButton();
        TitleBarIcon.Remove(this);
        SourceInitialized += (_, _) => FitToScreen();
    }

    /// <summary>
    /// 窗口出现之前执行一次：高度按第 5 步选了 .zip 来留，第 6 步也放得下。不超过屏幕，再居中。
    /// 屏幕放不下就占满工作区，第 5 步自己滚动。显示后不再改，所以不会跳。
    /// </summary>
    private void FitToScreen()
    {
        var area = SystemParameters.WorkArea;
        var handle = new WindowInteropHelper(this).Handle;
        GetWindowRect(handle, out var outer);
        GetClientRect(handle, out var inner);
        var frame = ((outer.Bottom - outer.Top) - (inner.Bottom - inner.Top)) / VisualTreeHelper.GetDpi(this).DpiScaleY;

        double needed = 0;
        Plans.MeasureWithZipOptions(() =>
        {
            Root.Measure(new Size(Width, double.PositiveInfinity));
            needed = Root.DesiredSize.Height - Plans.PackColumnShortfall() + frame;
            Root.InvalidateMeasure();
        });

        Height = Math.Min(Math.Max(needed, MinHeight), area.Height);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect32 rect);

    public void OpenTargets()
    {
        Plans.Flush();
        ToolWindow.ShowPage(this, "目的地设置", _targets ??= new TargetsPage(), 880, 720);
    }

    public void ShowPlan(string planId) => Plans.ShowPlan(planId);

    public void OpenQueue() => HoldClose(() =>
        ToolWindow.ShowPage(this, "查询记录", _queue ??= new QueuePage(), 1240, 840));

    /// <summary>
    /// 查询记录关掉的同时，主窗口有时也会收到关闭。这段时间里忽略这次关闭。
    /// 标志多留到对话框返回之后，避开随后才送到的那条关闭消息。
    /// </summary>
    private void HoldClose(Action open)
    {
        _holdClose = true;
        try
        {
            open();
        }
        finally
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => _holdClose = false));
        }
    }

    private void OnOpenTargets(object sender, RoutedEventArgs e) => OpenTargets();

    private void OnOpenAbout(object sender, RoutedEventArgs e)
    {
        Plans.Flush();
        ToolWindow.ShowPage(this, "关于", _about ??= new AboutPage(), 680, 620, fitContent: true);
    }

    private void OnOpenUpdate(object sender, RoutedEventArgs e)
    {
        Plans.Flush();
        if (App.Updates.Offer is { } offer) UpdateDialog.Show(this, offer);
    }

    private void SyncUpdateButton() =>
        UpdateHost.Visibility = App.Updates.Offer is null ? Visibility.Collapsed : Visibility.Visible;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_holdClose)
        {
            e.Cancel = true;
            return;
        }
        var app = (App)Application.Current;
        if (app.IsExiting) return;
        Plans.Flush();
        if (app.ShouldStayInTray())
        {
            e.Cancel = true;
            Bar.ReleaseSettledStatus();
            Hide();
            return;
        }
        base.OnClosing(e);
        app.ExitApp();
    }
}
