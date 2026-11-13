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
    private DateTime? _destinationStart;
    private double _realUnit;
    private double _shownUnit;
    private double _fromUnit;
    private double _trackTo = -1;
    private DateTime _glideFrom;
    private double _glideSeconds;
    private bool _awaitingEnd;
    private bool _rendering;
    private bool _shown;

    /// <summary>目的地这一段再快也要飞满这么久。本地文件夹常常一秒就复制完。</summary>
    private const double DestinationSeconds = 1;

    /// <summary>进度大约一秒才来一次。把这一跳摊开飞，避免一顿一顿。</summary>
    private const double SampleSeconds = 1;

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
            if (_destinationStart is not null) Move(0.70 + 0.30 * _shownUnit, animate: false);
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
        Place(App.Status.Flight, animate: false);
    }

    /// <summary>跟着进度放飞机。目的地飞完或这次没有上传段时，才解开定格并通知调用方。</summary>
    public void OnStatus()
    {
        var flight = App.Status.Flight;
        if (flight is not double progress)
        {
            if (ShouldGlideOut())
            {
                if (_destinationStart is null)
                {
                    _destinationStart = DateTime.UtcNow;
                    _shownUnit = 0;
                    _trackTo = -1;
                }
                _realUnit = 1;
                _awaitingEnd = true;
                StartGlide();
                return;
            }
            Thaw();
            return;
        }
        if (progress <= 0.70)
        {
            ClearDestination();
            Place(progress, animate: true);
            return;
        }
        var entered = _destinationStart is null;
        _destinationStart ??= DateTime.UtcNow;
        if (entered) _shownUnit = 0;
        _realUnit = Math.Clamp((progress - 0.70) / 0.30, 0, 1);
        StartGlide();
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
    /// 本地文件夹常常一秒传完，完成通知先到，界面还没看见上传进度。
    /// 这种时候先把目的地飞完，再刷新行李牌。
    /// </summary>
    private bool ShouldGlideOut()
    {
        if (App.Status.IsError || App.Status.Text == "已取消") return false;
        if (_destinationStart is not null) return _shownUnit < 0.999;
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

    /// <summary>按帧往前飞。新进度摊到大约一秒里，整段再快也不少于一秒。</summary>
    private void OnGlide(object? sender, EventArgs e)
    {
        if (_destinationStart is not { } start)
        {
            StopGlide();
            return;
        }
        var goal = Math.Clamp(_realUnit, 0, 1);
        if (Math.Abs(goal - _trackTo) > 1e-4)
        {
            _fromUnit = _shownUnit;
            _trackTo = goal;
            _glideFrom = DateTime.UtcNow;
            var elapsed = (_glideFrom - start).TotalSeconds;
            var span = Math.Max(0, goal - _fromUnit);
            _glideSeconds = goal >= 1
                ? Math.Max(DestinationSeconds - elapsed, 0.35)
                : Math.Max(SampleSeconds, span * DestinationSeconds);
        }
        var t = _glideSeconds <= 0 ? 1 : (DateTime.UtcNow - _glideFrom).TotalSeconds / _glideSeconds;
        _shownUnit = t >= 1 ? _trackTo : _fromUnit + (_trackTo - _fromUnit) * t;
        Move(0.70 + 0.30 * _shownUnit, animate: false);
        if (_awaitingEnd && _shownUnit >= 0.999) Thaw();
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
        if (!animate || !_shown)
        {
            _plane.BeginAnimation(Canvas.LeftProperty, null);
            Canvas.SetLeft(_plane, left);
            _shown = true;
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
        _destinationStart = null;
        _realUnit = 0;
        _shownUnit = 0;
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
        _shown = false;
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
