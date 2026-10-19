// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A level range with two handles
/// TODO: I copy osu! here so need to change to our styling
/// </summary>
public partial class LevelRangeSlider : Container
{
    private const float handle_width = 46;

    private static readonly Color[] spectrum = Enumerable.Range(0, BeatmapFilter.MAX_LEVEL + 1).Select(level => BeatmapUtility.ColorForDifficulty(level)).ToArray();

    public Reactive<int> Min { get; } = new Reactive<int>(0);
    public Reactive<int> Max { get; } = new Reactive<int>(BeatmapFilter.MAX_LEVEL);

    private readonly Box dimBelow;
    private readonly Box dimAbove;
    private readonly Handle minHandle;
    private readonly Handle maxHandle;

    // Which handle a drag is moving, or null.
    private Reactive<int>? dragging;

    /// <param name="palette">The colours.</param>
    /// <param name="shear">The lean for the track and handles; the game's own by default.</param>
    public LevelRangeSlider(YuukiColorPalette palette, Vector2? shear = null)
    {
        Vector2 lean = shear ?? UserInterfaceConstant.Shear;

        Height = 30;

        var track = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 6,
            Shear = lean
        };

        for (int i = 0; i < spectrum.Length - 1; i++)
        {
            float segment = 1f / (spectrum.Length - 1);

            track.Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                RelativePositionAxes = Axes.X,
                X = i * segment,
                // A hair wider so neighbouring segments never show a seam.
                Width = segment + 0.002f,
                ColorInfo = ColorInfo.GradientHorizontal(spectrum[i], spectrum[i + 1])
            });
        }

        track.Add(dimBelow = dimmer());
        track.Add(dimAbove = dimmer());

        // The handles sit half outside the track's ends, so the track is inset by half a handle.
        Padding = new MarginPadding { Left = handle_width / 2, Right = handle_width / 2 };

        Children = new Drawable[]
        {
            track,
            minHandle = new Handle(palette, lean),
            maxHandle = new Handle(palette, lean)
        };

        Min.BindValueChanged(_ => updateDisplay(), true);
        Max.BindValueChanged(_ => updateDisplay(), true);

        Add(new HoverClickSounds(null));
    }

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    private static Box dimmer() => new Box
    {
        RelativeSizeAxes = Axes.Both,
        RelativePositionAxes = Axes.X,
        Color = Color.Black,
        Alpha = 0.55f
    };

    private static float fraction(int level) => (float)level / BeatmapFilter.MAX_LEVEL;

    private static string label(int level) => level >= BeatmapFilter.MAX_LEVEL ? "∞" : level.ToString();

    private void updateDisplay()
    {
        float from = fraction(Min.Value);
        float to = fraction(Max.Value);

        dimBelow.X = 0;
        dimBelow.Width = from;
        dimAbove.X = to;
        dimAbove.Width = 1 - to;

        minHandle.X = from;
        minHandle.Text = label(Min.Value);
        maxHandle.X = to;
        maxHandle.Text = label(Max.Value);
    }

    public override bool OnDragStart(MouseButtonEvent e)
    {
        int level = levelAt(e.ScreenSpaceMousePosition);

        // The nearer handle; on a tie (the two together) the side the press is on decides.
        int toMin = Math.Abs(level - Min.Value);
        int toMax = Math.Abs(level - Max.Value);
        dragging = toMin < toMax || (toMin == toMax && level <= Min.Value) ? Min : Max;

        moveTo(level);
        return true;
    }

    public override bool OnDrag(MouseEvent e)
    {
        if (dragging == null)
            return false;

        moveTo(levelAt(e.ScreenSpaceMousePosition));
        return true;
    }

    public override bool OnDragEnd(MouseButtonEvent e)
    {
        dragging = null;
        return true;
    }

    private void moveTo(int level)
    {
        int before = dragging?.Value ?? 0;

        if (dragging == Min)
            Min.Value = Math.Min(level, Max.Value);
        else if (dragging == Max)
            Max.Value = Math.Max(level, Min.Value);

        // A notch per level stepped over, higher up the range, pitched down at the ends.
        if (dragging != null && dragging.Value != before)
        {
            float at = fraction(dragging.Value);
            uiSounds?.Play(UISound.SliderTick, at <= 0 || at >= 1 ? 0.7 : 1 + at * 0.2);
        }
    }

    private int levelAt(Vector2 screenPosition)
    {
        float width = DrawSize.X - Padding.Left - Padding.Right;
        if (width <= 0)
            return 0;

        float x = (ToLocalSpace(screenPosition).X - Padding.Left) / width;
        return (int)Math.Round(Math.Clamp(x, 0, 1) * BeatmapFilter.MAX_LEVEL);
    }

    /// <summary>
    /// A sheared tab riding the track, showing the level it's at.
    /// </summary>
    private partial class Handle : Container
    {
        private readonly SpriteText text;

        public string Text
        {
            set => text.Text = value;
        }

        public Handle(YuukiColorPalette palette, Vector2 shear)
        {
            Anchor = Anchor.CentreLeft;
            Origin = Anchor.Centre;
            RelativePositionAxes = Axes.X;
            Size = new Vector2(handle_width, 30);

            Children = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 6,
                    Shear = shear,
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = palette.Backdrop
                    }
                },
                text = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 16, FontWeight.Bold),
                    Color = Color.White
                }
            };
        }

        public override bool OnHover(MouseEvent e)
        {
            this.ScaleTo(1.08f, 150, Easing.OutQuint);
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            this.ScaleTo(1f, 150, Easing.OutQuint);
            return base.OnHoverLost(e);
        }
    }
}
