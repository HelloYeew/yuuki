// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class MusicProgressBar : SliderBar<double>
{
    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    /// <summary>
    /// Fired with the target fraction (0–1) whenever the user scrubs the bar.
    /// </summary>
    public Action<float>? Seeked;

    /// <summary>
    /// Track length in ms, used only to format the hover tooltip as a time.
    /// </summary>
    public double DisplayDurationMs { get; set; }

    private const float bar_base_height = 4;
    private const float bar_hover_height = 9;

    private readonly Color fillColor;

    private Container bar;
    private Box fill;

    // True while a programmatic display update is in flight, so it doesn't get echoed back as a seek.
    private bool applyingDisplay;
    private bool dragging;

    // Guards against the base's initial LoadComplete value-change firing a spurious seek before the
    // bar is actually interactive.
    private bool ready;

    public MusicProgressBar(Color fillColor)
    {
        this.fillColor = fillColor;

        // The container is a tall-ish hit area so the thin bar is still easy to grab; the visible bar
        // sits at the bottom edge and grows upward on hover.
        RelativeSizeAxes = Axes.X;
        Height = 12;

        MinValue = 0;
        MaxValue = 1;

        // The bar works in fractions of the whole track, so the framework's default of two decimal
        // places would quantize scrubbing into a hundred positions — about 1.8 seconds on a
        // three-minute track. A seek bar wants the full continuous range.
        DecimalPlaces = null;

        Add(bar = new Container
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            RelativeSizeAxes = Axes.X,
            Height = bar_base_height,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.Black.WithAlpha(0.4f)
                },
                fill = new Box
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0, 1),
                    Color = fillColor
                }
            }
        });
    }

    public override string TooltipText
    {
        get
        {
            if (DisplayDurationMs <= 0)
                return string.Empty;

            double atMs = GetFillFraction() * DisplayDurationMs;
            return $"{format(atMs)} / {format(DisplayDurationMs)}";
        }
    }

    private static string format(double ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    /// <summary>
    /// Sets the displayed fill from the live playback position without triggering a seek.
    /// </summary>
    public void SetProgressDisplay(float fraction)
    {
        // Don't fight the user while they're scrubbing.
        if (dragging)
            return;

        applyingDisplay = true;
        Current.Value = Math.Clamp(fraction, 0f, 1f);
        applyingDisplay = false;
    }

    protected override void LoadComplete()
    {
        // base.LoadComplete fires an initial value-change to sync visuals; let it update the fill but
        // not fire a seek.
        base.LoadComplete();
        ready = true;
    }

    protected override void OnValueChanged(double value)
    {
        fill.Width = (float)Math.Clamp(value, 0, 1);

        // Only user-driven changes (mouse/keyboard) after load should seek; the initial sync and
        // programmatic display updates must not.
        if (ready && !applyingDisplay)
            Seeked?.Invoke((float)Math.Clamp(value, 0, 1));
    }

    protected override void OnHovered()
    {
        bar.ResizeTo(new Vector2(1, bar_hover_height), 150, Easing.OutQuint);
        uiSounds?.Play(UISound.Hover);
    }

    protected override void OnHoverLost()
    {
        bar.ResizeTo(new Vector2(1, bar_base_height), 150, Easing.OutQuint);
        uiSounds?.Play(UISound.HoverLost);
    }

    public override bool OnDragStart(MouseButtonEvent e)
    {
        dragging = true;
        return base.OnDragStart(e);
    }

    public override bool OnDragEnd(MouseButtonEvent e)
    {
        dragging = false;
        return base.OnDragEnd(e);
    }
}
