// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Maths;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A read-only snapshot of the tunables a <see cref="CarouselLayoutStrategy"/> needs to place cards.
/// Passed by the carousel every frame so the strategy stays stateless.
/// </summary>
public readonly struct CarouselMetrics
{
    /// <summary>
    /// The carousel's current draw size (px).
    /// </summary>
    public readonly Vector2 DrawSize;

    /// <summary>
    /// The edge length of a card at scale 1 (px).
    /// </summary>
    public readonly float CardSize;

    /// <summary>
    /// Distance between the centres of two adjacent cards along the scroll axis (px).
    /// </summary>
    public readonly float CardSpacing;

    /// <summary>
    /// Scale applied to non-selected cards.
    /// </summary>
    public readonly float SideScale;

    /// <summary>
    /// Scale applied to the centred (selected) card.
    /// </summary>
    public readonly float SelectedScale;

    /// <summary>
    /// How far the selected card pops out of the resting line (px).
    /// </summary>
    public readonly float PopHeight;

    /// <summary>
    /// Distance over which cards fade near the edges (px).
    /// </summary>
    public readonly float EdgeFadeWidth;

    public CarouselMetrics(Vector2 drawSize, float cardSize, float cardSpacing, float sideScale, float selectedScale, float popHeight, float edgeFadeWidth)
    {
        DrawSize = drawSize;
        CardSize = cardSize;
        CardSpacing = cardSpacing;
        SideScale = sideScale;
        SelectedScale = selectedScale;
        PopHeight = popHeight;
        EdgeFadeWidth = edgeFadeWidth;
    }
}

/// <summary>
/// The resting on-screen placement of a single card, before any add/remove animation offset is applied.
/// </summary>
public readonly struct SlotLayout
{
    public readonly Vector2 Position;
    public readonly Vector2 Scale;
    public readonly float Alpha;

    public SlotLayout(Vector2 position, Vector2 scale, float alpha)
    {
        Position = position;
        Scale = scale;
        Alpha = alpha;
    }
}

/// <summary>
/// Encapsulates all orientation-dependent geometry of a <see cref="Carousel{TModel,TCard}"/>: the card
/// slot shape, the resting layout, and the mapping between pointer input and the logical scroll position.
/// Swapping the strategy (and reshaping cards) is all a carousel needs to flip between horizontal
/// coverflow and a vertical list.
/// </summary>
public abstract class CarouselLayoutStrategy
{
    /// <summary>
    /// The size given to each card slot (and thus the card, which fills it).
    /// </summary>
    public abstract Vector2 SlotSize(CarouselMetrics m);

    /// <summary>
    /// The resting placement for a card whose signed distance from the selection is
    /// <paramref name="offset"/> (in index units).
    /// </summary>
    public abstract SlotLayout Compute(float offset, CarouselMetrics m);

    /// <summary>Converts a drag delta (screen px) into a change in the logical scroll position (index units).</summary>
    public abstract double DragToPositionDelta(Vector2 mouseDelta, CarouselMetrics m);

    /// <summary>The index nearest a local-space point, used for click-to-select.</summary>
    public abstract int IndexAtLocal(Vector2 local, double position, CarouselMetrics m);

    /// <summary>Whether the slot at signed distance <paramref name="offset"/> is close enough to be worth loading.</summary>
    public abstract bool IsOnScreen(float offset, CarouselMetrics m);

    /// <summary>
    /// The offset (px) a card animates in from on a staggered entrance (and out to on exit), pointing in
    /// the direction that reads naturally for this orientation — up from below for the coverflow, in from
    /// the side for the vertical list.
    /// </summary>
    public abstract Vector2 EntranceOffset(CarouselMetrics m);

    protected static float SmoothStep(float t) => t * t * (3f - 2f * t);
}

/// <summary>
/// Square cards laid out left-to-right; the centred card pops up and scales (coverflow). This ports the
/// carousel's original geometry verbatim.
/// </summary>
public class HorizontalCarouselLayout : CarouselLayoutStrategy
{
    public override Vector2 SlotSize(CarouselMetrics m) => new Vector2(m.CardSize);

    public override SlotLayout Compute(float offset, CarouselMetrics m)
    {
        float x = offset * m.CardSpacing;

        float proximity = Math.Clamp(1f - Math.Abs(offset), 0f, 1f);
        float pop = SmoothStep(proximity);

        float scale = m.SideScale + (m.SelectedScale - m.SideScale) * pop;
        float y = -m.PopHeight * pop;

        float alpha = 1f;
        float half = m.DrawSize.X / 2f;
        if (half > 1f)
            alpha = Math.Clamp((half - Math.Abs(x)) / m.EdgeFadeWidth, 0f, 1f);

        return new SlotLayout(new Vector2(x, y), new Vector2(scale), alpha);
    }

    public override double DragToPositionDelta(Vector2 mouseDelta, CarouselMetrics m) => -mouseDelta.X / m.CardSpacing;

    public override int IndexAtLocal(Vector2 local, double position, CarouselMetrics m)
    {
        float centre = m.DrawSize.X / 2f;
        return (int)Math.Round(position + (local.X - centre) / m.CardSpacing);
    }

    public override bool IsOnScreen(float offset, CarouselMetrics m)
    {
        float half = m.DrawSize.X / 2f;

        if (half <= 1f)
            return Math.Abs(offset) <= 6f;

        return Math.Abs(offset * m.CardSpacing) <= half + m.CardSpacing;
    }

    // Coverflow cards drop in from below their resting line.
    public override Vector2 EntranceOffset(CarouselMetrics m) => new Vector2(0f, m.CardSize * 0.55f);
}

/// <summary>
/// Wide bars stacked top-to-bottom; the centred bar shifts out sideways and grows slightly
/// Owns its own bar dimensions so its layout, drag and click math stay self-consistent.
/// </summary>
public class VerticalCarouselLayout : CarouselLayoutStrategy
{
    /// <summary>Bar width as a multiple of the carousel region width, so bars bleed off the right edge.</summary>
    public float BarWidthFactor { get; set; } = 1.12f;

    /// <summary>Fallback bar width (px) used before the region has a measured size.</summary>
    public float MinBarWidth { get; set; } = 420f;

    /// <summary>Height of a bar card (px).</summary>
    public float BarHeight { get; set; } = 92f;

    /// <summary>
    /// Distance between the centres of two adjacent bars (px). Deliberately smaller than
    /// <see cref="BarHeight"/> so bars overlap and stack — the centre-front depth ordering makes each bar
    /// tuck behind the one nearer the selection, and the hollow rim shadow keeps the seam crisp.
    /// </summary>
    public float BarSpacing { get; set; } = 80f;

    /// <summary>
    /// The radius of the circle the bars ride on, as a multiple of the visible half-height.
    /// Smaller bends the wedge more sharply.
    /// </summary>
    public float CircleRadius { get; set; } = 3f;

    /// <summary>Extra scale added to the selected bar on top of 1 (kept subtle).</summary>
    public float SelectedBonus { get; set; } = 0.03f;

    /// <summary>Vertical distance over which bars fade near the top/bottom edges (px).</summary>
    public float EdgeFadeHeight { get; set; } = 130f;

    public override Vector2 SlotSize(CarouselMetrics m)
    {
        float width = m.DrawSize.X > 1f ? m.DrawSize.X * BarWidthFactor : MinBarWidth;
        return new Vector2(width, BarHeight);
    }

    public override SlotLayout Compute(float offset, CarouselMetrics m)
    {
        float y = offset * BarSpacing;
        float yAbs = Math.Abs(y);

        // carousel curve: bars ride a circle of radius (CircleRadius * halfHeight) whose center is
        // off to the right, so x = R - sqrt(R^2 - y^2). Scaling R with the viewport keeps the arc gentle
        // and smooth (no kink at the vertex).
        float halfHeight = Math.Max(1f, m.DrawSize.Y / 2f);
        float radius = CircleRadius * halfHeight;
        float discriminant = Math.Max(0f, radius * radius - y * y);
        float x = radius - MathF.Sqrt(discriminant);

        float proximity = Math.Clamp(1f - Math.Abs(offset), 0f, 1f);
        float scale = 1f + SelectedBonus * SmoothStep(proximity);

        float alpha = 1f;
        if (halfHeight > 1f)
            alpha = Math.Clamp((halfHeight - yAbs) / EdgeFadeHeight, 0f, 1f);

        return new SlotLayout(new Vector2(x, y), new Vector2(scale), alpha);
    }

    public override double DragToPositionDelta(Vector2 mouseDelta, CarouselMetrics m) => -mouseDelta.Y / BarSpacing;

    public override int IndexAtLocal(Vector2 local, double position, CarouselMetrics m)
    {
        float centre = m.DrawSize.Y / 2f;
        return (int)Math.Round(position + (local.Y - centre) / BarSpacing);
    }

    public override bool IsOnScreen(float offset, CarouselMetrics m)
    {
        float half = m.DrawSize.Y / 2f;

        if (half <= 1f)
            return Math.Abs(offset) <= 6f;

        return Math.Abs(offset * BarSpacing) <= half + BarSpacing;
    }

    // Bars slide in from off the right edge (the direction they already bleed toward).
    public override Vector2 EntranceOffset(CarouselMetrics m) => new Vector2(SlotSize(m).X * 0.16f, 0f);
}
