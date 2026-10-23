// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A read-only snapshot of what a <see cref="CarouselLayoutStrategy"/> needs to place cards.
/// Passed by the carousel every frame so the strategy stays stateless.
/// </summary>
public readonly struct CarouselMetrics
{
    /// <summary>
    /// The carousel's current draw size (px).
    /// </summary>
    public readonly Vector2 DrawSize;

    /// <summary>
    /// How far into skim mode the list is, 0 (at rest) to 1 (compacted); see <see cref="CarouselMotion"/>.
    /// </summary>
    public readonly float Skim;

    /// <summary>
    /// The look-ahead, -1 to 1: positive while moving towards later items, which moves the cue back so
    /// more of them show.
    /// </summary>
    public readonly float Lead;

    /// <summary>
    /// Room kept clear at the start of the list (px): a strip's items fade out before it, as they do at
    /// its ends. The docked song sits there.
    /// </summary>
    public readonly float StartInset;

    public CarouselMetrics(Vector2 drawSize, float skim = 0, float lead = 0, float startInset = 0)
    {
        DrawSize = drawSize;
        Skim = skim;
        Lead = lead;
        StartInset = startInset;
    }
}

/// <summary>
/// The resting on-screen placement of a single card, before any add/remove animation offset is applied.
/// <see cref="Position"/> is relative to the carousel's centre.
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
/// slot shape, the resting layout, the focus window, and the mapping between pointer input and the
/// logical scroll position. Swapping the strategy (and reshaping cards) is all a carousel needs to flip
/// between the horizontal strip and the vertical rail.
/// </summary>
public abstract class CarouselLayoutStrategy
{
    /// <summary>
    /// The size given to each card slot (and thus the card, which fills it).
    /// </summary>
    public abstract Vector2 SlotSize(CarouselMetrics m);

    /// <summary>
    /// The resting placement for a card whose signed distance from the scroll position is
    /// <paramref name="offset"/> (in index units).
    /// </summary>
    public abstract SlotLayout Compute(float offset, CarouselMetrics m);

    /// <summary>Converts a drag delta (the carousel's local px) into a change in the logical scroll position (index units).</summary>
    public abstract double DragToPositionDelta(Vector2 mouseDelta, CarouselMetrics m);

    /// <summary>
    /// The slot under a local-space point, used for click-to-select. Unclamped: past either end it is a
    /// slot with no card. Anywhere on the focus window is the focused slot.
    /// </summary>
    public abstract int IndexAtLocal(Vector2 local, double position, CarouselMetrics m);

    /// <summary>Whether the slot at signed distance <paramref name="offset"/> is close enough to be worth loading.</summary>
    public abstract bool IsOnScreen(float offset, CarouselMetrics m);

    /// <summary>
    /// The offset (px) a card animates in from on a staggered entrance (and out to on exit), pointing in
    /// the direction that reads naturally for this orientation: in from the side for the rail, up from
    /// below for the strip.
    /// </summary>
    public abstract Vector2 EntranceOffset(CarouselMetrics m);

    /// <summary>
    /// Where the focus window sits, in the carousel's local space. It never moves: the list scrolls
    /// behind it and the window shows whichever item is at the scroll position.
    /// </summary>
    public abstract RectangleF FocusWindow(CarouselMetrics m);

    /// <summary>
    /// How far in from each end the cards fade out, cut off there by the carousel's mask, or null for
    /// cards that aren't masked: those fade themselves, through the alpha <see cref="Compute"/> gives them.
    /// </summary>
    public virtual MarginPadding? MaskFade => null;

    protected static float SmoothStep(float t) => t * t * (3f - 2f * t);

    /// <summary>
    /// <paramref name="rest"/> at rest, moving to <paramref name="skimming"/> as the list skims; just
    /// <paramref name="rest"/> when the layout has no skim size for it.
    /// </summary>
    protected static float WhileSkimming(float rest, float? skimming, CarouselMetrics m) =>
        skimming is float s ? rest + (s - rest) * m.Skim : rest;

    /// <summary>
    /// How far along the scroll axis the slot at <paramref name="offset"/> sits from the cue. Every slot
    /// keeps one size; near the cue they part by the window's extra length, so the window always has its
    /// gap and a slot slides in and out from under it rather than overlapping its neighbours.
    /// </summary>
    protected static float AlongAxis(float offset, float pitch, float windowLength)
    {
        float s = SmoothStep(Math.Min(Math.Abs(offset), 1f));
        return offset * pitch + Math.Sign(offset) * (windowLength - pitch) / 2f * s;
    }

    /// <summary>
    /// Fades a slot out while it's under the focus window, so nothing of it shows round the window's
    /// rounded corners.
    /// </summary>
    protected static float UnderWindowAlpha(float offset) => Math.Clamp((Math.Abs(offset) - 0.3f) / 0.3f, 0f, 1f);

    /// <summary>
    /// The slot whose centre is within half a pitch of <paramref name="along"/> (the point's distance
    /// from the cue on the scroll axis), searching outwards from the scroll position.
    /// </summary>
    protected static int SlotAlong(float along, double position, float pitch, float windowLength, float halfExtent)
    {
        int reach = (int)Math.Ceiling(halfExtent / pitch) + 2;
        int centre = (int)Math.Round(position);

        for (int k = centre - reach; k <= centre + reach; k++)
        {
            if (Math.Abs(along - AlongAxis((float)(k - position), pitch, windowLength)) <= pitch / 2f)
                return k;
        }

        return (int)Math.Round(position + along / pitch);
    }
}

/// <summary>
/// The vertical rail: one row per song, all the same height, sliding behind a taller focus window fixed in
/// the middle. Nothing moves sideways.
/// </summary>
public class RailCarouselLayout : CarouselLayoutStrategy
{
    /// <summary>Distance between the centres of two rows (px), gap included.</summary>
    public float RowPitch { get; set; } = 64f;

    /// <summary>Space left between two rows (px).</summary>
    public float RowGap { get; set; } = 4f;

    /// <summary>Height of the focus window (px).</summary>
    public float WindowHeight { get; set; } = 140f;

    /// <summary>Space between the rows and the rail's edges (px).</summary>
    public float SideInset { get; set; } = 14f;

    /// <summary>Room kept clear on the right for the scrollbar (px).</summary>
    public float ScrollbarGutter { get; set; } = 28f;

    /// <summary>Distance over which rows fade out at the top and bottom of the list (px).</summary>
    public float TopFade { get; set; } = 48f;

    public float BottomFade { get; set; } = 64f;

    /// <summary>The row pitch while skimming, or null for rows that don't compact.</summary>
    public float? SkimRowPitch { get; set; }

    /// <summary>The window's height while skimming, or null for a window that doesn't compact.</summary>
    public float? SkimWindowHeight { get; set; }

    /// <summary>
    /// How far the cue moves against the direction of travel at full look-ahead, as a share of the rail's
    /// height.
    /// </summary>
    public float LeadShare { get; set; } = 0.07f;

    private float pitch(CarouselMetrics m) => WhileSkimming(RowPitch, SkimRowPitch, m);

    private float windowHeight(CarouselMetrics m) => WhileSkimming(WindowHeight, SkimWindowHeight, m);

    /// <summary>The cue's distance from the rail's middle (px): up, as the list moves on down.</summary>
    private float cue(CarouselMetrics m) => -LeadShare * m.DrawSize.Y * m.Lead;

    private float rowWidth(CarouselMetrics m) => Math.Max(1f, m.DrawSize.X - 2 * SideInset - ScrollbarGutter);

    public override Vector2 SlotSize(CarouselMetrics m) => new Vector2(rowWidth(m), pitch(m) - RowGap);

    public override SlotLayout Compute(float offset, CarouselMetrics m)
    {
        float y = cue(m) + AlongAxis(offset, pitch(m), windowHeight(m));

        // The rows are centred in the space left of the scrollbar gutter.
        return new SlotLayout(new Vector2(-ScrollbarGutter / 2f, y), Vector2.One, UnderWindowAlpha(offset));
    }

    // The rows run on to the rail's ends and fade there, rather than each fading whole before it reaches
    // one, which left up to a row and a half empty at the top.
    public override MarginPadding? MaskFade => new MarginPadding { Top = TopFade, Bottom = BottomFade };

    public override double DragToPositionDelta(Vector2 mouseDelta, CarouselMetrics m) => -mouseDelta.Y / pitch(m);

    public override int IndexAtLocal(Vector2 local, double position, CarouselMetrics m)
    {
        if (FocusWindow(m).Contains(local))
            return (int)Math.Round(position);

        return SlotAlong(local.Y - m.DrawSize.Y / 2f - cue(m), position, pitch(m), windowHeight(m), m.DrawSize.Y / 2f);
    }

    public override bool IsOnScreen(float offset, CarouselMetrics m)
    {
        float half = m.DrawSize.Y / 2f;

        if (half <= 1f)
            return Math.Abs(offset) <= 6f;

        return Math.Abs(cue(m) + AlongAxis(offset, pitch(m), windowHeight(m))) <= half + pitch(m);
    }

    // Rows slide in from the right, the side the rail comes in from.
    public override Vector2 EntranceOffset(CarouselMetrics m) => new Vector2(90f, 0f);

    public override RectangleF FocusWindow(CarouselMetrics m) =>
        new RectangleF(SideInset, m.DrawSize.Y / 2f + cue(m) - windowHeight(m) / 2f, rowWidth(m), windowHeight(m));
}

/// <summary>
/// The horizontal strip: one tile per song, all the same size, sliding behind a hero tile fixed in the
/// middle that rises out of the strip's top edge.
/// </summary>
public class StripCarouselLayout : CarouselLayoutStrategy
{
    /// <summary>Distance between the centres of two tiles (px).</summary>
    public float Pitch { get; set; } = 148f;

    /// <summary>A tile's size (px): its cover and the captions under it.</summary>
    public Vector2 TileSize { get; set; } = new Vector2(136f, 176f);

    /// <summary>The tiles' top edge, measured from the carousel's top (px).</summary>
    public float TileTop { get; set; } = 124f;

    /// <summary>The hero tile's size (px).</summary>
    public Vector2 WindowSize { get; set; } = new Vector2(248f, 292f);

    /// <summary>The hero tile's bottom edge, measured up from the carousel's bottom (px).</summary>
    public float WindowBottom { get; set; } = 36f;

    /// <summary>Distance over which tiles fade at the left and right ends (px).</summary>
    public float EdgeFade { get; set; } = 70f;

    /// <summary>The tile pitch while skimming, or null for tiles that don't compact.</summary>
    public float? SkimPitch { get; set; }

    /// <summary>A tile's size while skimming, or null for tiles that don't compact.</summary>
    public Vector2? SkimTileSize { get; set; }

    /// <summary>
    /// How far the cue moves against the direction of travel at full look-ahead, as a share of the strip's
    /// width.
    /// </summary>
    public float LeadShare { get; set; } = 0.06f;

    private float pitch(CarouselMetrics m) => WhileSkimming(Pitch, SkimPitch, m);

    private Vector2 tileSize(CarouselMetrics m) => new Vector2(
        WhileSkimming(TileSize.X, SkimTileSize?.X, m),
        WhileSkimming(TileSize.Y, SkimTileSize?.Y, m));

    /// <summary>The cue's distance from the strip's middle (px): left, as the list moves on right.</summary>
    private float cue(CarouselMetrics m) => -LeadShare * m.DrawSize.X * m.Lead;

    public override Vector2 SlotSize(CarouselMetrics m) => tileSize(m);

    public override SlotLayout Compute(float offset, CarouselMetrics m)
    {
        var tile = tileSize(m);
        float x = cue(m) + AlongAxis(offset, pitch(m), WindowSize.X);
        float y = -m.DrawSize.Y / 2f + TileTop + tile.Y / 2f;

        float alpha = 1f;
        float half = m.DrawSize.X / 2f;

        if (half > 1f)
        {
            float edge = x < 0 ? half - m.StartInset : half;
            alpha = Math.Clamp((edge - Math.Abs(x) - tile.X / 2f) / EdgeFade + 1f, 0f, 1f);
        }

        return new SlotLayout(new Vector2(x, y), Vector2.One, alpha * UnderWindowAlpha(offset));
    }

    public override double DragToPositionDelta(Vector2 mouseDelta, CarouselMetrics m) => -mouseDelta.X / pitch(m);

    public override int IndexAtLocal(Vector2 local, double position, CarouselMetrics m)
    {
        if (FocusWindow(m).Contains(local))
            return (int)Math.Round(position);

        return SlotAlong(local.X - m.DrawSize.X / 2f - cue(m), position, pitch(m), WindowSize.X, m.DrawSize.X / 2f);
    }

    public override bool IsOnScreen(float offset, CarouselMetrics m)
    {
        float half = m.DrawSize.X / 2f;

        if (half <= 1f)
            return Math.Abs(offset) <= 6f;

        return Math.Abs(cue(m) + AlongAxis(offset, pitch(m), WindowSize.X)) <= half + pitch(m);
    }

    // Tiles rise into place from below.
    public override Vector2 EntranceOffset(CarouselMetrics m) => new Vector2(0f, 60f);

    public override RectangleF FocusWindow(CarouselMetrics m) =>
        new RectangleF(m.DrawSize.X / 2f + cue(m) - WindowSize.X / 2f, m.DrawSize.Y - WindowBottom - WindowSize.Y, WindowSize.X, WindowSize.Y);
}
