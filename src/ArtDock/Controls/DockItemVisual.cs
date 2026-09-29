using System.Windows;
using System.Windows.Media;
using ArtDock.Dock;

namespace ArtDock.Controls;

/// <summary>
/// One icon in the dock.
///
/// Deliberately a bare <see cref="FrameworkElement"/> rather than a templated control: it is
/// drawn once at its resting size and then magnified purely by a render transform, so there
/// is no template, no layout and no re-raster on any frame of the wave.
/// </summary>
public sealed class DockItemVisual : FrameworkElement
{

    /// <summary>
    /// How far the icon fades at the bottom of a launch pulse.
    /// </summary>
    /// <remarks>
    /// The flash used to be a white plate drawn over the icon, which read as a square
    /// appearing on top of it rather than as the icon reacting. Pulsing the icon's own
    /// opacity leaves nothing on screen that was not already there — the icon itself is
    /// what moves.
    /// </remarks>
    private const double FlashDepth = 0.72;

    private static readonly Brush FallbackBrush = Frozen(Color.FromArgb(0xCC, 0x8A, 0x8F, 0xA8));

    /// <summary>The divider a separator item draws.</summary>
    private static readonly Brush SeparatorBrush = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

    /// <summary>
    /// How see-through a drop preview is. Low enough to read as "not there yet", high
    /// enough that the icon is still recognisable — which is the whole point of showing it.
    /// </summary>
    internal const double GhostOpacity = 0.45;

    /// <summary>
    /// Time constant of the slide between slots, in milliseconds.
    /// </summary>
    /// <remarks>
    /// An exponential rather than a fixed-length tween, because a slot can be retargeted
    /// mid-flight — the insertion point of a drag moves while the icons are still settling
    /// — and a tween would have to be restarted, which is what makes re-aimed animations
    /// stutter. Exponential decay simply changes where it is heading.
    /// </remarks>
    private const double SlotEaseMs = 60;

    /// <summary>Below this, in slots, the icon is treated as arrived.</summary>
    private const double SlotEpsilon = 0.001;

    /// <summary>
    /// Time constant of the growth an arriving icon makes, in milliseconds.
    /// </summary>
    /// <remarks>
    /// A shade quicker than the slide the icons either side of it are making, so the slot
    /// finishes opening at about the moment the icon finishes filling it.
    /// </remarks>
    private const double EntryEaseMs = 50;

    /// <summary>Above this, the icon is treated as fully grown.</summary>
    private const double EntryEpsilon = 0.002;

    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _translate = new(0, 0);

    private double _flashIntensity;

    /// <summary>How much of its resting size the icon is currently drawn at, 0 to 1.</summary>
    private double _entryScale = 1;

    public DockItemVisual(DockItem item, double restingSize, bool isGhost = false)
    {
        Item = item;
        IsGhost = isGhost;
        RestingSize = restingSize;

        if (isGhost)
        {
            // A drop preview, drawn exactly as the real item will be — same icon, same
            // slot, same magnification — only faded, so what the drop is about to produce
            // is visible before it happens.
            Opacity = GhostOpacity;
        }

        // WPF hit-testing does nothing for this control and one active harm. The dock is
        // WS_EX_NOACTIVATE, so WPF routes no mouse input to it at all — clicks are read from
        // the window procedure and the pointer is polled — but an icon that can be hit is an
        // element an OLE drag can be *over*. Splicing a drop preview into the row rebuilds
        // it, and the element under the cursor going away raises DragLeave on the way out,
        // from inside the very handler that added the preview. The drop logic read that as
        // the drag leaving and took the preview down between one mouse move and the next,
        // which put it back: measured on the live dock at five times a second, with the
        // pointer never leaving the window. With nothing under the cursor but the window
        // itself, the only DragLeave left is a real one.
        IsHitTestVisible = false;

        Width = restingSize;
        Height = restingSize;

        // Scale about the bottom centre so icons grow upward out of the bar, the way the
        // macOS Dock does, instead of expanding around their middle.
        RenderTransformOrigin = new Point(0.5, 1);
        RenderTransform = new TransformGroup { Children = { _scale, _translate } };

        // The shell hands back 128px icons that are drawn into roughly a 31-41px box, a 3-4x
        // minification. WPF's default bilinear sampling reads a 2x2 neighbourhood, so at that
        // ratio it simply misses most of the source pixels — edges alias, and because the
        // ratio changes continuously as the icon magnifies, they crawl while the wave moves.
        // Fant resampling averages over the whole footprint instead.
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public DockItem Item { get; private set; }

    /// <summary>The slot this icon is heading for, counted from the bar's left.</summary>
    public double TargetSlot { get; private set; }

    /// <summary>Where it is drawn right now, easing toward <see cref="TargetSlot"/>.</summary>
    public double CurrentSlot { get; private set; }

    /// <summary>True while the icon still has ground to cover.</summary>
    public bool IsSlotMoving => Math.Abs(TargetSlot - CurrentSlot) > SlotEpsilon;

    /// <summary>Puts the icon in a slot outright, with nothing to animate.</summary>
    public void SnapToSlot(double slot)
    {
        TargetSlot = slot;
        CurrentSlot = slot;
    }

    /// <summary>Sends the icon to a slot, to be slid into over the next few frames.</summary>
    public void MoveToSlot(double slot) => TargetSlot = slot;

    /// <summary>Advances the slide. Pure state — the caller pushes it onto the transform.</summary>
    /// <returns>True while the icon is still moving, including the frame it arrives on.</returns>
    public bool AdvanceSlot(double deltaMs)
    {
        if (!IsSlotMoving)
        {
            CurrentSlot = TargetSlot;
            return false;
        }

        if (deltaMs > 0)
        {
            CurrentSlot += (TargetSlot - CurrentSlot) * (1 - Math.Exp(-deltaMs / SlotEaseMs));
            if (!IsSlotMoving)
            {
                CurrentSlot = TargetSlot;
            }
        }

        // Deliberately true even on the frame it arrives, so the settled position gets
        // painted once rather than being left a fraction of a pixel out.
        return true;
    }

    /// <summary>
    /// Starts the icon from nothing, so it grows into its slot as the row opens one for it.
    /// </summary>
    /// <remarks>
    /// The scale is anchored at the bottom centre, the same as the wave's, so an arriving
    /// icon rises out of the bar rather than swelling around its middle. Only a drop preview
    /// uses this: everything else in the dock is either already there or replacing something
    /// that was.
    /// </remarks>
    public void BeginEntry() => _entryScale = 0;

    /// <summary>True while the icon is still growing into its slot.</summary>
    public bool IsEntering => _entryScale < 1 - EntryEpsilon;

    /// <summary>How much of its resting size the icon is drawn at while it arrives.</summary>
    public double EntryScale => _entryScale;

    /// <summary>Advances the growth. Same exponential shape, and same reasons, as the slide.</summary>
    /// <returns>True while it is still growing, including the frame it finishes on.</returns>
    public bool AdvanceEntry(double deltaMs)
    {
        if (!IsEntering)
        {
            _entryScale = 1;
            return false;
        }

        if (deltaMs > 0)
        {
            _entryScale += (1 - _entryScale) * (1 - Math.Exp(-deltaMs / EntryEaseMs));
            if (!IsEntering)
            {
                _entryScale = 1;
            }
        }

        return true;
    }

    /// <summary>
    /// Points the visual at a new model without rebuilding it.
    /// </summary>
    /// <remarks>
    /// Settings changes hand the dock freshly built <see cref="DockItem"/>s even when
    /// nothing about an icon changed. Rebinding rather than recreating is what lets a
    /// visual keep its slot animation across a rebuild — and what stops the dock flickering
    /// every time a drop preview moves.
    /// </remarks>
    public void Rebind(DockItem item)
    {
        if (ReferenceEquals(Item, item))
        {
            return;
        }

        Item = item;
        InvalidateVisual();
    }

    /// <summary>True when this is a drop preview rather than a pinned item.</summary>
    public bool IsGhost { get; }

    /// <summary>The size this icon is drawn at; magnification is a transform on top of it.</summary>
    public double RestingSize { get; private set; }

    /// <summary>
    /// Resizes the icon in place.
    /// </summary>
    /// <remarks>
    /// Lets a change to icon size re-use the existing visuals instead of discarding and
    /// rebuilding every one of them, which is what made dragging the size slider stutter.
    /// </remarks>
    public void SetRestingSize(double size)
    {
        if (Math.Abs(RestingSize - size) < 0.01)
        {
            return;
        }

        RestingSize = size;
        Width = size;
        Height = size;
        InvalidateVisual();
    }



    /// <summary>
    /// Launch-flash strength, 0 to 1. Driven by <see cref="DockBar"/> on the same tick
    /// as the wave.
    /// </summary>
    public double FlashIntensity
    {
        get => _flashIntensity;
        set
        {
            if (Math.Abs(_flashIntensity - value) < 0.001)
            {
                return;
            }

            _flashIntensity = value;
            InvalidateVisual();
        }
    }

    /// <summary>Applies one frame of the wave. Pure render state — never triggers layout.</summary>
    /// <remarks>
    /// <para>
    /// A separator is held at its resting size. It is punctuation rather than a target: there
    /// is nothing to see more of, and nothing to click, so growing it only draws the eye to
    /// the one thing in the row that is not an icon.
    /// </para>
    /// <para>
    /// Only the drawing is pinned; the slot still widens with the wave, and the divider stays
    /// centred in it. That is deliberate. The widths are what must sum to a constant for the
    /// bar to spread without changing size — see <see cref="Dock.DockLayout"/> — and taking
    /// one slot out of that sum would set the whole bar breathing as the pointer passed over
    /// it.
    /// </para>
    /// </remarks>
    public void ApplyWave(double magnifiedSize, double deltaX)
    {
        var factor = Item.IsSeparator ? 1 : magnifiedSize / RestingSize;

        // An icon still arriving is drawn at a fraction of whatever the wave asked for, so
        // it grows in under the wave rather than instead of it.
        _scale.ScaleX = factor * _entryScale;
        _scale.ScaleY = factor * _entryScale;
        _translate.X = deltaX;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(0, 0, RestingSize, RestingSize);
        var radius = RestingSize * 0.22;

        if (Item.IsSeparator)
        {
            DrawSeparator(drawingContext);
            return;
        }

        // The launch pulse and the disabled dimming are both just opacity, so they
        // multiply rather than fighting over the same pixels.
        var opacity = (Item.IsDisabled ? 0.4 : 1) * (1 - (_flashIntensity * FlashDepth));
        var faded = opacity < 0.999;

        if (faded)
        {
            drawingContext.PushOpacity(opacity);
        }

        if (Item.Icon is { } icon)
        {
            // A little breathing room, so neighbouring glyphs do not crowd each other at
            // peak magnification where their boxes sit only a gap apart.
            var inset = RestingSize * 0.08;
            drawingContext.DrawImage(
                icon,
                Fit(icon, new Rect(inset, inset, RestingSize - (inset * 2), RestingSize - (inset * 2))));
        }
        else
        {
            // No icon resolved (uninstalled target, or the shell had nothing to give).
            drawingContext.DrawRoundedRectangle(FallbackBrush, pen: null, bounds, radius, radius);
        }

        if (faded)
        {
            drawingContext.Pop();
        }
    }

    /// <summary>
    /// The largest rectangle of the image's shape that fits the box, centred in it.
    /// </summary>
    /// <remarks>
    /// An icon is square and fills the box exactly. A picture's thumbnail has the picture's
    /// shape, and so may an image the user chose; both were stretched to a square before,
    /// which squashed a landscape photo flat.
    /// </remarks>
    private static Rect Fit(ImageSource image, Rect box)
    {
        if (image.Width <= 0 || image.Height <= 0 || Math.Abs(image.Width - image.Height) < 0.01)
        {
            return box;
        }

        var scale = Math.Min(box.Width / image.Width, box.Height / image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;
        return new Rect(box.X + ((box.Width - width) / 2), box.Y + ((box.Height - height) / 2), width, height);
    }

    /// <summary>
    /// Draws a divider: a thin rounded bar down the middle of the slot.
    /// </summary>
    /// <remarks>
    /// Deliberately shorter than the slot, so it reads as a rule between groups rather than
    /// as a wall. It is drawn at the resting size and stays there while the wave passes —
    /// see <see cref="ApplyWave"/> — so the icons either side of it grow around a rule that
    /// holds still.
    /// </remarks>
    private void DrawSeparator(DrawingContext drawingContext)
    {
        var thickness = Math.Max(1.5, RestingSize * 0.055);
        var height = RestingSize * 0.62;

        drawingContext.DrawRoundedRectangle(
            SeparatorBrush,
            pen: null,
            new Rect(
                (RestingSize - thickness) / 2,
                (RestingSize - height) / 2,
                thickness,
                height),
            thickness / 2,
            thickness / 2);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
