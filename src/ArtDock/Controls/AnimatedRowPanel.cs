using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ArtDock.Controls;

/// <summary>
/// A vertical stack whose children slide to their new places instead of jumping to them.
/// </summary>
/// <remarks>
/// <para>
/// For the pinned list, which reorders under the pointer while a row is being dragged. A row
/// that simply appeared in its new place would leave the reader to work out what had moved;
/// sliding says it. The dock does the same thing for the same reason — see
/// <see cref="DockItemVisual.MoveToSlot"/> — driving its slide from the render loop it is
/// already running, where this can hand the work to the animation system instead.
/// </para>
/// <para>
/// The slide is a render transform over a settled layout: every child is arranged at its real
/// place, and only its drawing is offset. That matters for more than tidiness — the drag
/// works out which row the pointer is over from arranged positions, so a row half way through
/// a slide must not move the answer.
/// </para>
/// </remarks>
internal sealed class AnimatedRowPanel : Panel
{
    /// <summary>
    /// How long a row takes to reach its new place.
    /// </summary>
    /// <remarks>
    /// Short enough to keep up with a pointer crossing rows quickly, long enough to be seen
    /// as movement rather than as a jump.
    /// </remarks>
    private const double SlideMs = 160;

    /// <summary>Where each child was last arranged, so a change can be animated from it.</summary>
    private readonly Dictionary<UIElement, double> _arrangedAt = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        // Height is whatever the rows want; width is the panel's, so a row fills it and can
        // be grabbed anywhere along its length.
        var forChild = new Size(availableSize.Width, double.PositiveInfinity);

        var widest = 0d;
        var total = 0d;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(forChild);
            widest = Math.Max(widest, child.DesiredSize.Width);
            total += child.DesiredSize.Height;
        }

        return new Size(double.IsInfinity(availableSize.Width) ? widest : availableSize.Width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0d;

        foreach (UIElement child in InternalChildren)
        {
            var height = child.DesiredSize.Height;
            child.Arrange(new Rect(0, y, finalSize.Width, height));

            SlideFromPrevious(child, y);
            y += height;
        }

        ForgetDepartedChildren();

        return finalSize;
    }

    /// <summary>Starts a child sliding from where it used to be to where it now is.</summary>
    private void SlideFromPrevious(UIElement child, double arrangedAt)
    {
        var known = _arrangedAt.TryGetValue(child, out var previous);
        _arrangedAt[child] = arrangedAt;

        // A child arranged for the first time is simply where it is. Animating one in from
        // nowhere would make opening the page look like a list being dealt out.
        if (!known || Math.Abs(previous - arrangedAt) < 0.5)
        {
            return;
        }

        if (child.RenderTransform is not TranslateTransform slide || slide.IsFrozen)
        {
            slide = new TranslateTransform();
            child.RenderTransform = slide;
        }

        // From the old position to none at all. Stated outright rather than left to the
        // property's current value, which a previous slide may still be holding.
        slide.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(previous - arrangedAt, 0, TimeSpan.FromMilliseconds(SlideMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void ForgetDepartedChildren()
    {
        if (_arrangedAt.Count <= InternalChildren.Count)
        {
            return;
        }

        var live = new HashSet<UIElement>();
        foreach (UIElement child in InternalChildren)
        {
            live.Add(child);
        }

        foreach (var gone in _arrangedAt.Keys.Where(child => !live.Contains(child)).ToList())
        {
            _arrangedAt.Remove(gone);
        }
    }
}
