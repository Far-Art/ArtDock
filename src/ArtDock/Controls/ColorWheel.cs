using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArtDock.Controls;

/// <summary>
/// An HSV colour picker: a hue-and-saturation wheel with a brightness bar beside it.
/// </summary>
/// <remarks>
/// <para>
/// Hand-drawn because WPF ships no colour picker, and the alternative is taking a
/// dependency for one control.
/// </para>
/// <para>
/// The wheel is a bitmap generated once at full brightness and left there. It used to dim
/// under a black wash as the brightness came down, which is what HSV says a wheel at that
/// value looks like — and it made the control unusable for what this picker is for. A dock
/// bar is usually a dark colour, so the wheel spent most of its life as a murky disc with
/// the hues washed out of exactly the range people pick from. The wheel is the hue and
/// saturation control; brightness belongs to the bar beside it, and what was actually
/// chosen is shown by the swatch above.
/// </para>
/// </remarks>
public sealed class ColorWheel : FrameworkElement
{
    private const double WheelSize = 148;
    private const double BarWidth = 18;
    private const double BarGap = 12;

    /// <summary>
    /// Resolution of the generated wheel. Larger than it is drawn, so the disc stays clean
    /// when the dialog is on a high-DPI display.
    /// </summary>
    private const int WheelPixels = 296;

    private static readonly Lazy<BitmapSource> Wheel = new(() => CreateWheel(WheelPixels));

    private static readonly Pen MarkerPen = FrozenPen(Colors.White, 2);
    private static readonly Pen MarkerShadowPen = FrozenPen(Color.FromArgb(0x99, 0, 0, 0), 3.5);
    private static readonly Brush BarBorderBrush = FrozenBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

    /// <summary>Which control the pointer took hold of, so a drag keeps acting on it.</summary>
    private enum Grab
    {
        None,
        Wheel,
        Bar
    }

    private Grab _grab;

    private double _hue;
    private double _saturation;
    private double _value = 1;

    public ColorWheel()
    {
        Focusable = false;
        SnapsToDevicePixels = true;
    }

    /// <summary>Raised whenever the user moves either control.</summary>
    public event EventHandler<Color>? ColorPicked;

    /// <summary>
    /// The colour shown. Setting it moves both controls; a pure grey keeps whatever hue the
    /// wheel was already on, since grey has no hue of its own to restore and snapping the
    /// marker back to red would be a lie about what the user last chose.
    /// </summary>
    public Color Color
    {
        get => FromHsv(_hue, _saturation, _value);
        set
        {
            var (hue, saturation, brightness) = ToHsv(value);
            _hue = saturation <= 0 ? _hue : hue;
            _saturation = saturation;
            _value = brightness;
            InvalidateVisual();
        }
    }

    private static Rect WheelBounds => new(0, 0, WheelSize, WheelSize);

    private static Rect BarBounds => new(WheelSize + BarGap, 0, BarWidth, WheelSize);

    protected override Size MeasureOverride(Size availableSize) =>
        new(WheelSize + BarGap + BarWidth, WheelSize);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var wheel = WheelBounds;

        drawingContext.DrawImage(Wheel.Value, wheel);

        DrawWheelMarker(drawingContext, wheel);
        DrawBar(drawingContext);
    }

    private void DrawWheelMarker(DrawingContext drawingContext, Rect wheel)
    {
        var radius = wheel.Width / 2;
        var angle = _hue * Math.PI / 180;
        var centre = new Point(
            radius + (Math.Cos(angle) * _saturation * radius),
            radius + (Math.Sin(angle) * _saturation * radius));

        // Outlined twice: a dark ring under a white one, so the marker stays visible over
        // both the pale centre of the wheel and its saturated rim.
        drawingContext.DrawEllipse(brush: null, MarkerShadowPen, centre, 5.5, 5.5);
        drawingContext.DrawEllipse(brush: null, MarkerPen, centre, 5.5, 5.5);
    }

    private void DrawBar(DrawingContext drawingContext)
    {
        var bar = BarBounds;

        var gradient = new LinearGradientBrush(
            FromHsv(_hue, _saturation, 1),
            Colors.Black,
            new Point(0, 0),
            new Point(0, 1));
        gradient.Freeze();

        drawingContext.DrawRoundedRectangle(gradient, pen: null, bar, 4, 4);
        drawingContext.DrawRoundedRectangle(
            brush: null, new Pen(BarBorderBrush, 1), bar, 4, 4);

        var y = bar.Top + ((1 - _value) * bar.Height);
        var left = new Point(bar.Left - 2, y);
        var right = new Point(bar.Right + 2, y);

        drawingContext.DrawLine(MarkerShadowPen, left, right);
        drawingContext.DrawLine(MarkerPen, left, right);
    }

    // ---- input ---------------------------------------------------------------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        var point = e.GetPosition(this);
        _grab = BarBounds.Contains(point) ? Grab.Bar : Grab.Wheel;

        // Captured so a drag that leaves the control keeps steering it — running off the
        // rim of the wheel should track the hue round, not stop dead at the edge.
        CaptureMouse();
        Pick(point);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_grab != Grab.None && e.LeftButton == MouseButtonState.Pressed)
        {
            Pick(e.GetPosition(this));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        _grab = Grab.None;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Pick(Point point)
    {
        if (_grab == Grab.Bar)
        {
            _value = 1 - Math.Clamp((point.Y - BarBounds.Top) / BarBounds.Height, 0, 1);
        }
        else
        {
            var radius = WheelSize / 2;
            var dx = point.X - radius;
            var dy = point.Y - radius;

            _hue = ((Math.Atan2(dy, dx) * 180 / Math.PI) + 360) % 360;
            _saturation = Math.Clamp(Math.Sqrt((dx * dx) + (dy * dy)) / radius, 0, 1);
        }

        InvalidateVisual();
        ColorPicked?.Invoke(this, Color);
    }

    // ---- the wheel bitmap ----------------------------------------------------

    /// <summary>
    /// Paints the hue/saturation disc: hue around the rim, saturation out from the centre,
    /// at full brightness.
    /// </summary>
    private static BitmapSource CreateWheel(int size)
    {
        var stride = size * 4;
        var pixels = new byte[stride * size];
        var radius = size / 2.0;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x + 0.5 - radius;
                var dy = y + 0.5 - radius;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));

                // One pixel of coverage at the rim, so the disc has a clean edge rather
                // than a staircase.
                var coverage = Math.Clamp(radius - distance, 0, 1);
                if (coverage <= 0)
                {
                    continue;
                }

                var colour = FromHsv(
                    ((Math.Atan2(dy, dx) * 180 / Math.PI) + 360) % 360,
                    Math.Min(1, distance / radius),
                    1);

                // Pbgra32 is premultiplied, so the channels carry the coverage too.
                var alpha = (byte)(coverage * 255);
                var index = (y * stride) + (x * 4);
                pixels[index] = (byte)(colour.B * alpha / 255);
                pixels[index + 1] = (byte)(colour.G * alpha / 255);
                pixels[index + 2] = (byte)(colour.R * alpha / 255);
                pixels[index + 3] = alpha;
            }
        }

        var bitmap = BitmapSource.Create(
            size, size, 96, 96, PixelFormats.Pbgra32, palette: null, pixels, stride);

        bitmap.Freeze();
        return bitmap;
    }

    // ---- colour space --------------------------------------------------------

    /// <summary>Hue in degrees, saturation and value 0 to 1.</summary>
    public static Color FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        var chroma = value * saturation;
        var sector = hue / 60;
        var second = chroma * (1 - Math.Abs((sector % 2) - 1));
        var match = value - chroma;

        var (r, g, b) = (int)sector switch
        {
            0 => (chroma, second, 0d),
            1 => (second, chroma, 0d),
            2 => (0d, chroma, second),
            3 => (0d, second, chroma),
            4 => (second, 0d, chroma),
            _ => (chroma, 0d, second)
        };

        return Color.FromRgb(
            (byte)Math.Round((r + match) * 255),
            (byte)Math.Round((g + match) * 255),
            (byte)Math.Round((b + match) * 255));
    }

    /// <summary>The inverse of <see cref="FromHsv"/>.</summary>
    public static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var chroma = max - min;

        double hue;
        if (chroma <= 0)
        {
            hue = 0;
        }
        else if (max == r)
        {
            hue = 60 * (((g - b) / chroma) % 6);
        }
        else if (max == g)
        {
            hue = 60 * (((b - r) / chroma) + 2);
        }
        else
        {
            hue = 60 * (((r - g) / chroma) + 4);
        }

        return (((hue % 360) + 360) % 360, max <= 0 ? 0 : chroma / max, max);
    }

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(FrozenBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
