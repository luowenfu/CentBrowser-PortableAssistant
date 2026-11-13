using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CentBrowser_PortableAssistant.Core.Backup;

namespace CentBrowser_PortableAssistant.Views;

/// <summary>行李牌上的飞机，以及飞行中定格的那几行字。</summary>
public sealed class TagPlane
{
    private readonly Grid _grid;
    private readonly Canvas _layer;
    private readonly Image _plane;
    private readonly Line _start;
    private readonly Line _extract;
    private readonly Line _pack;
    private readonly Button _backup;
    private readonly TextBlock _size;
    private readonly List<(DependencyObject Target, DependencyProperty Property, BindingBase Binding)> _held = [];
    private readonly Action _arrived;
    private double _real;
    private double _shownProgress;
    private int _stage;
    private DateTime _stageStart;
    private bool _started;
    private double _fromUnit;
    private double _trackTo = -1;
    private DateTime _glideFrom;
    private double _glideSeconds;
    private bool _awaitingEnd;
    private bool _rendering;
    private bool _placed;

    /// <summary>提取、打包再快也要飞满这么久。</summary>
    private const double ShortSeconds = 0.5;

    /// <summary>目的地再快也要飞满这么久。</summary>
    private const double DestinationSeconds = 1;

    /// <summary>这一段已经飞够最短时间，最后一点不用再拖满。</summary>
    private const double TailSeconds = 0.35;

    /// <summary>0 提取起点，0.20 打包起点，0.70 目的地起点，1 终点。</summary>
    private static readonly double[] Bounds = [0, 0.20, 0.70, 1];

    public TagPlane(
        Grid grid, Canvas layer, Image plane,
        Line start, Line extract, Line pack, Button backup, TextBlock size,
        TextBlock planName, TextBlock packedSize, ItemsControl files,
        StackPanel destination, TextBlock destinationText, TextBlock problem, Action arrived)
    {
        _grid = grid;
        _layer = layer;
        _plane = plane;
        _start = start;
        _extract = extract;
        _pack = pack;
        _backup = backup;
        _size = size;
        _arrived = arrived;
        Holds = [planName, packedSize, files, destination, destinationText, problem, pack];
        grid.SizeChanged += (_, _) =>
        {
            if (_started) Move(_shownProgress, animate: false);
            else Place(App.Status.Flight, animate: false);
        };
    }

    /// <summary>要断开绑定的控件。虚线用可见性，其余用各自的文字或列表。</summary>
    private UIElement[] Holds { get; }

    public bool Frozen { get; private set; }

    /// <summary>先放开上一次定格，刷新文件名，再按牌上现在的字定格。</summary>
    public void Retake(Action? refreshNames, string? rawSize)
    {
        ClearDestination();
        if (Frozen) Release();
        refreshNames?.Invoke();
        if (rawSize is not null) _size.Text = rawSize;
        Hold();
        Place(App.Status.Flight is null ? null : 0d, animate: false);
    }

    /// <summary>跟着进度放飞机。三段都飞完，才解开定格并通知调用方。</summary>
    public void OnStatus()
    {
        var flight = App.Status.Flight;
        if (flight is not double progress)
        {
            if (ShouldGlideOut())
            {
                EnsureStarted();
                _real = 1;
                _awaitingEnd = true;
                StartGlide();
                return;
            }
            Thaw();
            return;
        }
        EnsureStarted();
        _real = Math.Clamp(progress, 0, 1);
        StartGlide();
    }

    /// <summary>从提取起点开始计时。已经在飞就不动。</summary>
    private void EnsureStarted()
    {
        if (_started) return;
        _started = true;
        _shownProgress = 0;
        _stage = 0;
        _stageStart = DateTime.UtcNow;
        _trackTo = -1;
        _real = 0;
    }

    private void Hold()
    {
        foreach (var element in Holds) HoldOne(element);
        Frozen = true;
    }

    private void HoldOne(UIElement element)
    {
        switch (element)
        {
            case TextBlock text:
                Keep(text, TextBlock.TextProperty);
                break;
            case ItemsControl list:
                Keep(list, ItemsControl.ItemsSourceProperty);
                break;
            default:
                Keep(element, UIElement.VisibilityProperty);
                break;
        }
    }

    private void Keep(DependencyObject target, DependencyProperty property)
    {
        var binding = BindingOperations.GetBindingBase(target, property);
        if (binding is null) return;
        var current = target.GetValue(property);
        if (current is System.Collections.IEnumerable items and not string)
            current = items.Cast<object>().ToList();
        _held.Add((target, property, binding));
        BindingOperations.ClearBinding(target, property);
        target.SetValue(property, current);
    }

    private void Release()
    {
        foreach (var (target, property, binding) in _held)
            BindingOperations.SetBinding(target, property, binding);
        _held.Clear();
        Frozen = false;
    }

    /// <summary>
    /// 某一段常常不到一秒就做完，完成通知先到，飞机还没飞完。
    /// 这种时候按段飞完再刷新行李牌。出错和取消不等。
    /// </summary>
    private bool ShouldGlideOut()
    {
        if (App.Status.IsError || App.Status.Text == "已取消") return false;
        if (_started && _shownProgress < 0.999) return true;
        if (!Frozen) return false;
        var text = App.Status.Text;
        return text.Contains("已送达", StringComparison.Ordinal) || text.Contains("备份完成", StringComparison.Ordinal);
    }

    private void StartGlide()
    {
        if (_rendering) return;
        _rendering = true;
        CompositionTarget.Rendering += OnGlide;
    }

    private void StopGlide()
    {
        if (!_rendering) return;
        _rendering = false;
        CompositionTarget.Rendering -= OnGlide;
    }

    /// <summary>按帧往前飞。提取和打包再快也不少于半秒，目的地不少于一秒。</summary>
    private void OnGlide(object? sender, EventArgs e)
    {
        if (!_started)
        {
            StopGlide();
            return;
        }
        var start = Bounds[_stage];
        var end = Bounds[_stage + 1];
        var width = end - start;
        var goal = Math.Clamp((_real - start) / width, 0, 1);
        var shownUnit = Math.Clamp((_shownProgress - start) / width, 0, 1);
        if (Math.Abs(goal - _trackTo) > 1e-4)
        {
            _fromUnit = shownUnit;
            _trackTo = goal;
            _glideFrom = DateTime.UtcNow;
            var elapsed = (_glideFrom - _stageStart).TotalSeconds;
            var span = Math.Max(0, goal - _fromUnit);
            var least = _stage == 2 ? DestinationSeconds : ShortSeconds;
            _glideSeconds = goal >= 1
                ? Math.Max(least - elapsed, TailSeconds)
                : Math.Max(least, span * least);
        }
        var t = _glideSeconds <= 0 ? 1 : (DateTime.UtcNow - _glideFrom).TotalSeconds / _glideSeconds;
        var unit = t >= 1 ? _trackTo : _fromUnit + (_trackTo - _fromUnit) * t;
        _shownProgress = start + width * unit;
        Move(_shownProgress, animate: false);
        if (_stage < 2 && unit >= 0.999 && _real >= end - 1e-9)
        {
            _stage++;
            _stageStart = DateTime.UtcNow;
            _trackTo = -1;
            _shownProgress = Bounds[_stage];
            return;
        }
        if (_awaitingEnd && _stage == 2 && unit >= 0.999) Thaw();
    }

    private void Place(double? progress, bool animate)
    {
        if (progress is not double value)
        {
            Hide();
            return;
        }
        Move(value, animate);
    }

    private void Move(double progress, bool animate)
    {
        var imageWidth = ImageWidth();
        if (imageWidth <= 0 || !TryBounds(out var x0, out var x1, out var x2, out var x3)) return;
        var left = ImageLeft(progress, x0, x1, x2, x3, imageWidth);
        var top = PlaneTop();
        _layer.Clip = new RectangleGeometry(new Rect(x0, top - 8, x3 - x0, Math.Max(_layer.ActualHeight, _grid.ActualHeight) - top + _plane.Height + 16));
        Canvas.SetTop(_plane, top);
        _plane.Visibility = Visibility.Visible;
        if (!animate || !_placed)
        {
            _plane.BeginAnimation(Canvas.LeftProperty, null);
            Canvas.SetLeft(_plane, left);
            _placed = true;
            return;
        }
        _plane.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(left, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>贴近行李牌上沿，留出一点缝。</summary>
    private double PlaneTop()
    {
        var pad = _grid.Parent is Border border ? border.Padding.Top : 0;
        return 3 - pad;
    }

    private void ClearDestination()
    {
        StopGlide();
        _started = false;
        _real = 0;
        _shownProgress = 0;
        _stage = 0;
        _stageStart = default;
        _fromUnit = 0;
        _trackTo = -1;
        _awaitingEnd = false;
    }

    /// <summary>飞机停了，牌上的字恢复绑定。大小交给调用方重新估算。</summary>
    private void Thaw()
    {
        ClearDestination();
        var thaw = Frozen;
        if (thaw) Release();
        Hide();
        if (thaw) _arrived();
    }

    private void Hide()
    {
        _plane.BeginAnimation(Canvas.LeftProperty, null);
        _plane.Visibility = Visibility.Collapsed;
        _layer.Clip = null;
        _placed = false;
    }

    private bool TryBounds(out double x0, out double x1, out double x2, out double x3)
    {
        x0 = Middle(_start);
        x1 = Middle(_extract);
        x2 = Middle(_pack);
        x3 = _backup.TranslatePoint(new Point(-16, 0), _layer).X;
        return _start.ActualHeight > 0 && _extract.ActualHeight > 0 && _pack.IsVisible && _backup.ActualWidth > 0
            && x0 < x1 && x1 < x2 && x2 < x3;
    }

    private double Middle(FrameworkElement line) => line.TranslatePoint(new Point(line.ActualWidth / 2, 0), _layer).X;

    /// <summary>前两段机头贴分割线。最后一段多飞出一张图，到 100% 时图片左缘贴在终点上。</summary>
    private static double ImageLeft(double progress, double x0, double x1, double x2, double x3, double imageWidth) =>
        FlightProgress.Nose(progress, x0, x1, x2, x3 + imageWidth) - imageWidth;

    private double ImageWidth()
    {
        if (_plane.Source is not BitmapSource bitmap || bitmap.PixelHeight <= 0) return 0;
        return _plane.Height * bitmap.PixelWidth / bitmap.PixelHeight;
    }
}
