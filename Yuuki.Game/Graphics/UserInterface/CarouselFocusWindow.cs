// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// One item drawn in a <see cref="CarouselFocusWindow{TModel}"/>'s large layout.
/// </summary>
public abstract partial class CarouselFocusFace<TModel> : Container
    where TModel : class
{
    public TModel? Model { get; private set; }

    protected CarouselFocusFace()
    {
        RelativeSizeAxes = Axes.Both;
    }

    public void Apply(TModel model)
    {
        Model = model;

        // A list filled during its owner's load is laid out (and its window shown) before it has loaded
        // itself. Until this face loads, nothing it resolves is there yet, and its cover can't start
        // loading, so it fills itself in on load instead.
        if (IsLoaded)
            OnApply(model);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (Model != null)
            OnApply(Model);
    }

    protected abstract void OnApply(TModel model);

    /// <summary>
    /// Reshapes the face for the carousel's orientation: a wide card in the rail, a hero tile in the strip.
    /// </summary>
    public virtual void SetOrientation(CarouselOrientation orientation)
    {
    }

    /// <summary>
    /// Reprints the face in its model's current colours, which may have become known after it was applied.
    /// </summary>
    public virtual void RefreshColors()
    {
    }
}

/// <summary>
/// The fixed window on a carousel's cue, and the only solid thing in the list. The cards slide behind it,
/// and it shows whichever item is at the scroll position in a large layout. Between two items it doesn't
/// grow or slide continuously: it holds still for most of each step and then clicks over to the next one,
/// so a fast scroll reads as a flip counter rather than two half-faded cards smeared across it.
/// </summary>
public abstract partial class CarouselFocusWindow<TModel> : Container
    where TModel : class
{
    /// <summary>
    /// How far (as a share of the window's length on the scroll axis) a face slides as it swaps.
    /// </summary>
    private const float swap_travel = 0.4f;

    private const double fill_fade_duration = 180;

    // The visible card. The window itself carries the entrance and exit transforms, so the per-frame
    // work below never touches its alpha.
    private readonly Container body;
    private readonly Box fill;
    private readonly Container faceLayer;

    private readonly List<(int Index, CarouselFocusFace<TModel> Face)> live = new List<(int, CarouselFocusFace<TModel>)>();
    private readonly Stack<CarouselFocusFace<TModel>> spare = new Stack<CarouselFocusFace<TModel>>();
    private readonly List<(int Index, CarouselFocusFace<TModel> Face)> stillShown = new List<(int, CarouselFocusFace<TModel>)>();

    private CarouselOrientation orientation = CarouselOrientation.Horizontal;
    private TModel? colouredFor;
    private Color fillColor = Color.Gray;

    protected CarouselFocusWindow()
    {
        RelativeSizeAxes = Axes.Both;

        Child = body = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Color = Color.Black.WithAlpha(0.35f),
                Radius = 18f,
                Offset = new Vector2(0f, 10f)
            },
            Children = new Drawable[]
            {
                fill = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.Gray
                },
                faceLayer = new Container
                {
                    RelativeSizeAxes = Axes.Both
                }
            }
        };

        SetOrientation(orientation);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // The fill may have been picked before anything it reads was resolved; see CarouselFocusFace.Apply.
        RefreshColors();
    }

    protected abstract CarouselFocusFace<TModel> CreateFace();

    /// <summary>
    /// The window's fill behind <paramref name="model"/>. The face prints its text in a darker shade of it.
    /// </summary>
    protected abstract Color FillFor(TModel model);

    /// <summary>
    /// The item the window shows (the one at the rounded scroll position), if any.
    /// </summary>
    public TModel? ShownItem => colouredFor;

    /// <summary>
    /// The fill the window wears (or is fading to) for <see cref="ShownItem"/>.
    /// </summary>
    public Color FillColor => fillColor;

    public void SetOrientation(CarouselOrientation value)
    {
        orientation = value;
        body.CornerRadius = value == CarouselOrientation.Vertical ? 16f : 20f;

        foreach (var (_, face) in live)
            face.SetOrientation(value);
        foreach (var face in spare)
            face.SetOrientation(value);
    }

    /// <summary>
    /// Lays the window out for this frame: <paramref name="position"/> is the carousel's (continuous)
    /// scroll position over <paramref name="items"/>.
    /// </summary>
    public void Display(IReadOnlyList<TModel> items, double position)
    {
        if (items.Count == 0)
        {
            body.Alpha = 0;
            stillShown.Clear();
            releaseAllBut(stillShown);
            colouredFor = null;
            return;
        }

        body.Alpha = 1;

        int first = (int)Math.Floor(position);
        float t = smoothStep(Math.Clamp(((float)(position - first) - 0.3f) / 0.4f, 0f, 1f));
        float travel = (orientation == CarouselOrientation.Vertical ? DrawHeight : DrawWidth) * swap_travel;

        stillShown.Clear();
        place(items, first, -t * travel, 1f - t);
        place(items, first + 1, (1f - t) * travel, t);
        releaseAllBut(stillShown);

        var current = items[Math.Clamp((int)Math.Round(position), 0, items.Count - 1)];

        if (!ReferenceEquals(current, colouredFor))
        {
            fadeFill(FillFor(current), colouredFor == null ? 0 : fill_fade_duration);
            colouredFor = current;
        }
    }

    /// <summary>
    /// Recolours the window and the faces on it, for when an item's colours became known after it was shown.
    /// </summary>
    protected void RefreshColors()
    {
        if (colouredFor != null)
        {
            var color = FillFor(colouredFor);

            if (color != fillColor)
                fadeFill(color, fill_fade_duration);
        }

        foreach (var (_, face) in live)
            face.RefreshColors();
    }

    private void fadeFill(Color color, double duration)
    {
        fill.FadeToColor(color, duration, Easing.OutQuint);
        fillColor = color;
    }

    private void place(IReadOnlyList<TModel> items, int index, float offset, float alpha)
    {
        if (index < 0 || index >= items.Count || alpha <= 0.001f)
            return;

        var face = faceFor(index, items[index]);
        face.Position = orientation == CarouselOrientation.Vertical ? new Vector2(0f, offset) : new Vector2(offset, 0f);
        face.Alpha = alpha;
        stillShown.Add((index, face));
    }

    private CarouselFocusFace<TModel> faceFor(int index, TModel model)
    {
        foreach (var (i, face) in live)
        {
            if (i == index && ReferenceEquals(face.Model, model))
                return face;
        }

        var fresh = spare.Count > 0 ? spare.Pop() : createFace();
        fresh.Apply(model);
        fresh.Alpha = 1;
        live.Add((index, fresh));
        return fresh;
    }

    private CarouselFocusFace<TModel> createFace()
    {
        var face = CreateFace();
        face.SetOrientation(orientation);
        faceLayer.Add(face);
        return face;
    }

    private void releaseAllBut(List<(int Index, CarouselFocusFace<TModel> Face)> keep)
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            if (keep.Contains(live[i]))
                continue;

            live[i].Face.Alpha = 0;
            spare.Push(live[i].Face);
            live.RemoveAt(i);
        }
    }

    private static float smoothStep(float x) => x * x * (3f - 2f * x);
}
