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
    private bool _shown;

    public TagPlane(
        Grid grid, Canvas layer, Image plane,
        Line start, Line extract, Line pack, Button backup, TextBlock size,
        TextBlock planName, TextBlock packedSize, ItemsControl files,
        StackPanel destination, TextBlock destinationText, TextBlock problem)
    {
        _grid = grid;
        _layer = layer;
        _plane = plane;
        _start = start;
        _extract = extract;
        _pack = pack;
        _backup = backup;
        _size = size;
        Holds = [planName, packedSize, files, destination, destinationText, problem, pack];
        grid.SizeChanged += (_, _) => Place(animate: false);
    }

    /// <summary>要断开绑定的控件。虚线用可见性，其余用各自的文字或列表。</summary>
    private UIElement[] Holds { get; }

    public bool Frozen { get; private set; }

    /// <summary>先放开上一次定格，刷新文件名，再按牌上现在的字定格。</summary>
    public void Retake(Action? refreshNames, string? rawSize)
    {
        if (Frozen) Release();
        refreshNames?.Invoke();
        if (rawSize is not null) _size.Text = rawSize;
        Hold();
        Place(animate: false);
    }

    /// <summary>进度没了就解开定格。返回值表示这次解开了，调用方再去估算大小。</summary>
    public bool OnStatus()
    {
        var thawed = false;
        if (App.Status.Flight is null && Frozen)
        {
            Release();
            thawed = true;
        }
        Place(animate: true);
        return thawed;
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

    private void Place(bool animate)
    {
        var flight = App.Status.Flight;
        var imageWidth = ImageWidth();
        if (flight is not double progress || imageWidth <= 0 || !TryBounds(out var x0, out var x1, out var x2, out var x3))
        {
            if (flight is null) Hide();
            return;
        }
        var nose = FlightProgress.Nose(progress, x0, x1, x2, x3);
        var left = nose - imageWidth;
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
