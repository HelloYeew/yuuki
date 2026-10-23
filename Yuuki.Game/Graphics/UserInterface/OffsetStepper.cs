// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// An offset to nudge a few ms at a time, big enough to tap: its name, lower it, what it is, raise it.
/// </summary>
public partial class OffsetStepper : FlowContainer
{
    /// <summary>How far one press moves it, in ms.</summary>
    public const int STEP = 5;

    /// <summary>
    /// The offset shown, in ms: set it to show another, and watch it for the player's steps.
    /// </summary>
    public Reactive<int> Current { get; } = new Reactive<int>(0);

    private readonly int min;
    private readonly int max;
    private readonly SpriteText value;

    /// <param name="palette">The colors to draw in.</param>
    /// <param name="label">Its name, shown before it; empty for none.</param>
    /// <param name="min">The lowest it goes.</param>
    /// <param name="max">The highest it goes.</param>
    /// <param name="textColor">The text's color: the palette's text by default, its light text over the dark.</param>
    public OffsetStepper(YuukiColorPalette palette, string label, int min, int max, Color? textColor = null)
    {
        this.min = min;
        this.max = max;

        Color text = textColor ?? palette.Text;

        AutoSizeAxes = Axes.Both;
        Direction = FlowDirection.Horizontal;
        Spacing = new Vector2(12, 0);

        if (label.Length > 0)
        {
            Add(new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = label,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 20, FontWeight.Regular),
                Color = text,
                Margin = new MarginPadding { Right = 4 }
            });
        }

        AddRange(new Drawable[]
        {
            new PillButton(palette.Control, palette.TextLight)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = "−",
                Action = () => step(-STEP)
            },
            // Wide enough for "-300 ms", so the buttons don't move as it changes.
            new Container
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Size = new Vector2(96, 30),
                Child = value = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 22, FontWeight.Bold),
                    Color = text
                }
            },
            new PillButton(palette.Control, palette.TextLight)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = "+",
                Action = () => step(STEP)
            }
        });

        Current.ValueChanged += e => value.Text = OffsetManager.Signed(e.NewValue);
        value.Text = OffsetManager.Signed(Current.Value);
    }

    private void step(int ms) => Current.Value = Math.Clamp(Current.Value + ms, min, max);
}
