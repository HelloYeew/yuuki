// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// A small toggle on a More info card: a label, with a dot of color before it if given, on a light pill that stands out
/// from the card (1.5:1 against the play palette's surface; the old faint fill was 1.1:1, all but gone). On, it says what
/// it stands for is showing; off, it fades, its dot and all. One look for all of them, whatever they toggle.
/// </summary>
public partial class ResultsChip : ClickableContainer
{
    private const float height = 30;

    private readonly Box background;
    private readonly SpriteText label;
    private readonly Circle? dot;
    private bool on;
    private bool onAnyway;
    private bool hovered;

    public bool On
    {
        get => on;
        set
        {
            on = value;
            updateLook();
        }
    }

    /// <summary>
    /// Whether it looks on while it isn't: one of a set where picking none shows them all (the lanes), with none picked.
    /// </summary>
    public bool OnAnyway
    {
        get => onAnyway;
        set
        {
            onAnyway = value;
            updateLook();
        }
    }

    /// <summary>
    /// Whether it looks on.
    /// </summary>
    public bool LooksOn => on || onAnyway;

    public string Text
    {
        get => label.Text;
        set => label.Text = value;
    }

    /// <summary>
    /// Called with whether it's on, each time it's clicked.
    /// </summary>
    public Action<bool>? Toggled { get; set; }

    public ResultsChip(string text, YuukiColorPalette palette, Color? dotColor = null, float chipHeight = height)
    {
        Height = chipHeight;
        AutoSizeAxes = Axes.X;
        Masking = true;
        CornerRadius = 8;

        var row = new FlowContainer
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            AutoSizeAxes = Axes.Both,
            Direction = FlowDirection.Horizontal,
            Spacing = new Vector2(7, 0),
            Padding = new MarginPadding { Left = 11, Right = 11 }
        };

        if (dotColor is Color color)
        {
            row.Add(dot = new Circle
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Size = new Vector2(10),
                Color = color
            });
        }

        row.Add(label = new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Text = text,
            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 14, FontWeight.ExtraBold),
            Color = palette.Text
        });

        Children = new Drawable[]
        {
            background = new Box { RelativeSizeAxes = Axes.Both },
            row
        };

        Action = () =>
        {
            On = !On;
            Toggled?.Invoke(On);
        };

        updateLook();
    }

    public override bool OnHover(MouseEvent e)
    {
        hovered = true;
        updateLook();
        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        hovered = false;
        updateLook();
        return base.OnHoverLost(e);
    }

    private void updateLook()
    {
        background.Color = Color.White.WithAlpha(LooksOn ? hovered ? 0.75f : 0.55f : hovered ? 0.32f : 0.2f);

        float alpha = LooksOn ? 1 : 0.45f;
        label.Alpha = alpha;

        if (dot != null)
            dot.Alpha = alpha;
    }
}
