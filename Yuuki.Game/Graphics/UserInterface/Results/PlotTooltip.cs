// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// What's under the pointer on a graph, in a dark pill above it: a line of text, with a dot of a judgement's color first
/// if it's about one.
/// </summary>
public partial class PlotTooltip : Container
{
    private static readonly Color background = ColorExtensions.FromHex("#0A0C18");
    private static readonly Color ink = ColorExtensions.FromHex("#F3E7D8");

    private readonly Circle dot;
    private readonly SpriteText text;

    public PlotTooltip()
    {
        AutoSizeAxes = Axes.Both;
        Origin = Anchor.BottomCentre;
        Masking = true;
        CornerRadius = 10;
        Alpha = 0;
        AlwaysPresent = true;

        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = background,
                Alpha = 0.92f
            },
            new FlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(7, 0),
                Padding = new MarginPadding { Left = 12, Right = 12, Top = 8, Bottom = 8 },
                Children = new Drawable[]
                {
                    dot = new Circle
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(10)
                    },
                    text = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Bold),
                        Color = ink
                    }
                }
            }
        };
    }

    public override bool HandlePositionalInput => false;

    public string Text => text.Text;

    /// <summary>
    /// Shows <paramref name="line"/> above <paramref name="at"/> (in its parent's space), with a dot of
    /// <paramref name="color"/> first if given.
    /// </summary>
    public void ShowAt(Vector2 at, string line, Color? color = null)
    {
        text.Text = line;
        dot.Alpha = color.HasValue ? 1 : 0;
        dot.Width = color.HasValue ? 10 : 0;

        if (color is Color c)
            dot.Color = c;

        Position = at - new Vector2(0, 14);
        Alpha = 1;
    }

    public void Dismiss() => Alpha = 0;
}
