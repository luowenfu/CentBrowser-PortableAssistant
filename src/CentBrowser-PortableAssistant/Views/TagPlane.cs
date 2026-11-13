using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _pace;
    private DateTime? _destinationStart;
    private double _realUnit;
    private bool _awaitingEnd;
    private bool _coasting;
    private int _coast;
    private bool _shown;

    /// <summary>目的地这一段再快也要飞满这么久。本地文件夹常常一秒就复制完。</summary>
    private const double DestinationSeconds = 2;

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
        _pace = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _pace.Tick += (_, _) => OnPace();
        grid.SizeChanged += (_, _) =>
        {
            if (_coasting || (_destinationStart is not null && _realUnit >= 1)) BeginCoast();
            else if (_destinationStart is not null) DrawDestination();
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
            if (_destinationStart is not null && !App.Status.IsError)
            {
                _realUnit = 1;
                _awaitingEnd = true;
                if (!_coasting) BeginCoast();
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
        _destinationStart ??= DateTime.UtcNow;
        _realUnit = Math.Clamp((progress - 0.70) / 0.30, 0, 1);
        if (_coasting) return;
        if (_realUnit >= 1)
        {
            BeginCoast();
            return;
        }
        DrawDestination();
        if (VisualUnit() + 1e-4 < _realUnit) StartPace();
        else StopPace();
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

    /// <summary>真实进度和已用时间里取更慢的那个。时间按整段两秒匀速。</summary>
    private double VisualUnit()
    {
        if (_destinationStart is not { } start) return 0;
        var paced = (DateTime.UtcNow - start).TotalSeconds / DestinationSeconds;
        return Math.Min(_realUnit, paced);
    }

    private void OnPace()
    {
        if (_coasting || _destinationStart is null)
        {
            StopPace();
            return;
        }
        if (_realUnit >= 1)
        {
            BeginCoast();
            return;
        }
        DrawDestination();
        if (VisualUnit() + 1e-4 >= _realUnit) StopPace();
    }

    /// <summary>进度已经到头时，用剩下的时间匀速滑到终点。不满两秒就补满。</summary>
    private void BeginCoast()
    {
        if (_destinationStart is not { } start) return;
        var imageWidth = ImageWidth();
        if (imageWidth <= 0 || !TryBounds(out var x0, out var x1, out var x2, out var x3)) return;
        StopPace();
        var remain = DestinationSeconds - (DateTime.UtcNow - start).TotalSeconds;
        var id = ++_coast;
        if (remain <= 0.03)
        {
            _coasting = false;
            Move(1, animate: false);
            if (_awaitingEnd) Thaw();
            return;
        }
        var shown = 0.70 + 0.30 * VisualUnit();
        var from = FlightProgress.Nose(shown, x0, x1, x2, x3) - imageWidth;
        Move(shown, animate: false);
        _coasting = true;
        var animation = new DoubleAnimation(from, x3 - imageWidth, TimeSpan.FromSeconds(remain))
        {
            FillBehavior = FillBehavior.HoldEnd,
        };
        animation.Completed += (_, _) =>
        {
            if (id != _coast) return;
            _coasting = false;
            if (_awaitingEnd) Thaw();
        };
        _plane.BeginAnimation(Canvas.LeftProperty, animation);
    }

    private void DrawDestination()
    {
        if (_destinationStart is null) return;
        var shown = 0.70 + 0.30 * VisualUnit();
        Move(shown, animate: false);
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
        var left = FlightProgress.Nose(progress, x0, x1, x2, x3) - imageWidth;
        _layer.Clip = new RectangleGeometry(new Rect(x0, 0, x3 - x0, Math.Max(_layer.ActualHeight, _grid.ActualHeight)));
        Canvas.SetTop(_plane, Math.Max(0, (_grid.ActualHeight - _plane.Height) / 2));
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

    private void StartPace()
    {
        if (!_pace.IsEnabled) _pace.Start();
    }

    private void StopPace()
    {
        if (_pace.IsEnabled) _pace.Stop();
    }

    private void ClearDestination()
    {
        _coast++;
        _coasting = false;
        StopPace();
        _destinationStart = null;
        _realUnit = 0;
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

    private double ImageWidth()
    {
        if (_plane.Source is not BitmapSource bitmap || bitmap.PixelHeight <= 0) return 0;
        return _plane.Height * bitmap.PixelWidth / bitmap.PixelHeight;
    }
}
