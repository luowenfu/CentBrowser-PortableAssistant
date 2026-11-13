using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace CentBrowser_PortableAssistant.Tray;

public enum TrayMark
{
    None,
    /// <summary>正在提取，这期间打开浏览器会打断备份。</summary>
    Collect,
    /// <summary>正在打包或上传，可以照常用浏览器。</summary>
    Busy,
    Fail,
    /// <summary>数据有变化，这一周期还没备份成功。</summary>
    Due,
}

/// <summary>托盘图标：悬停看状态，小圆点看进度，右键菜单做常用操作。</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _backupMenu;
    private readonly Icon _base;
    private readonly Dictionary<TrayMark, Icon> _marks = new();
    private TrayMark _mark = TrayMark.None;

    public event Action? OpenRequested;
    /// <summary>方案 id 为空表示全部。第二个参数为真表示快速。</summary>
    public event Action<string?, bool>? BackupRequested;
    public event Action? RetryRequested;
    public event Action? RecordsRequested;
    public event Action? ExitRequested;
    public event Action? Hovering;

    public TrayIcon()
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
        using (stream)
        {
            _base = new Icon(stream, SystemInformation.SmallIconSize);
        }
        _icon = new NotifyIcon { Icon = _base, Text = App.TrayTitle(), Visible = true };

        _backupMenu = new ToolStripMenuItem("立即备份");

        var menu = new ContextMenuStrip { ShowImageMargin = true };
        menu.Items.Add("打开主界面", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(_backupMenu);
        menu.Items.Add("立即重试上传", null, (_, _) => RetryRequested?.Invoke());
        menu.Items.Add("查询记录", null, (_, _) => RecordsRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.MouseMove += (_, _) => Hovering?.Invoke();
    }

    public void SetPlans(IEnumerable<(string Id, string Name)> plans)
    {
        var list = plans.ToList();
        _backupMenu.DropDownItems.Clear();
        if (list.Count != 1)
        {
            _backupMenu.DropDownItems.Add(PlanMenu("全部方案", null));
            if (list.Count > 0) _backupMenu.DropDownItems.Add(new ToolStripSeparator());
        }
        foreach (var (id, name) in list)
            _backupMenu.DropDownItems.Add(PlanMenu(name, id));
    }

    private ToolStripMenuItem PlanMenu(string title, string? planId)
    {
        var item = new ToolStripMenuItem(title);
        item.DropDownItems.Add("快速备份", null, (_, _) => BackupRequested?.Invoke(planId, true));
        item.DropDownItems.Add("慢速备份", null, (_, _) => BackupRequested?.Invoke(planId, false));
        return item;
    }

    /// <summary>托盘悬停文字，最多 127 个字符。</summary>
    public void SetTip(string text) => _icon.Text = text.Length > 127 ? text[..127] : text;

    public void SetMark(TrayMark mark)
    {
        if (mark == _mark) return;
        _mark = mark;
        _icon.Icon = mark == TrayMark.None ? _base : MarkIcon(mark);
    }

    public void Dispose()
    {
        var shown = _icon.Icon;
        _icon.Visible = false;
        _icon.Icon = null!;
        _icon.Dispose();
        if (!ReferenceEquals(_base, shown)) _base.Dispose();
        foreach (var icon in _marks.Values)
            if (!ReferenceEquals(icon, shown)) icon.Dispose();
        shown?.Dispose();
    }

    private Icon MarkIcon(TrayMark mark)
    {
        if (_marks.TryGetValue(mark, out var icon)) return icon;
        icon = Paint(_base, mark switch
        {
            TrayMark.Collect => Color.FromArgb(242, 153, 48),
            TrayMark.Busy => Color.FromArgb(47, 128, 237),
            TrayMark.Due => Color.FromArgb(138, 143, 152),
            _ => Color.FromArgb(214, 69, 69),
        });
        _marks[mark] = icon;
        return icon;
    }

    private static Icon Paint(Icon source, Color color)
    {
        using var bmp = source.ToBitmap();
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var diameter = Math.Max(6, bmp.Width / 2);
            var penWidth = Math.Max(1f, bmp.Width / 16f);
            var rect = new RectangleF(bmp.Width - diameter, bmp.Height - diameter, diameter - 1, diameter - 1);
            using var brush = new SolidBrush(color);
            using var pen = new Pen(Color.White, penWidth);
            g.FillEllipse(brush, rect);
            g.DrawEllipse(pen, rect);
        }
        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
