// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// Your best result on a song or difficulty as a small sheared chip: the rank in its color, with a medal
/// joined on for a full combo or an all perfect ("S", "A | FC", "S | AP"). Hidden when you haven't cleared
/// it.
/// </summary>
/// <remarks>
/// Each blade is a fill with tone-on-tone ink, as on the focus windows, so the chip carries its own colors
/// whatever it sits on. The medals have colors apart from every rank's (see
/// <see cref="JudgementColors.FULL_COMBO"/>), so a medal never merges into its rank.
/// </remarks>
public partial class BestMark : FlowContainer
{
    private readonly Blade rank;
    private readonly Blade medal;

    public BestMark(float textSize = 12.5f)
    {
        AutoSizeAxes = Axes.Both;
        Direction = FlowDirection.Horizontal;
        Spacing = new Vector2(textSize * 0.16f, 0);
        Alpha = 0;

        // The shear pushes each end's corner out past the chip's box by a third of its height (about
        // 0.6 of the text size), so keep that clear of whatever sits beside it.
        Margin = new MarginPadding { Left = textSize * 0.6f, Right = textSize * 0.6f };

        Children = new Drawable[]
        {
            rank = new Blade(textSize),
            medal = new Blade(textSize)
        };
    }

    /// <summary>
    /// One blade of the chip: its label, and the fill and ink it's printed in.
    /// </summary>
    public readonly record struct Piece(string Label, Color Fill, Color Ink);

    /// <summary>
    /// The chip for <paramref name="best"/>: its rank, and its medal when it has one (an all perfect over a
    /// full combo). Null when there's no best to show.
    /// </summary>
    public static (Piece Rank, Piece? Medal)? PiecesFor(ScoreInfo? best)
    {
        if (best == null)
            return null;

        var rank = piece(best.Rank.ToString(), JudgementColors.For(best.Rank));

        if (best.IsAllPerfect)
            return (rank, piece("AP", JudgementColors.ALL_PERFECT));

        if (best.IsFullCombo)
            return (rank, piece("FC", JudgementColors.FULL_COMBO));

        return (rank, null);
    }

    private static Piece piece(string label, Color color) => new Piece(label, SetlistColors.TierFill(color), SetlistColors.TierInk(color));

    public void SetBest(ScoreInfo? best)
    {
        if (PiecesFor(best) is not { } pieces)
        {
            Alpha = 0;
            return;
        }

        Alpha = 1;
        rank.Apply(pieces.Rank);

        // Alpha 0 also takes the medal out of the flow.
        medal.Alpha = pieces.Medal == null ? 0 : 1;

        if (pieces.Medal is { } shown)
            medal.Apply(shown);
    }

    /// <summary>
    /// A label on a sheared fill: one blade of the chip. A strip's tile prints its lanes on one too.
    /// </summary>
    public partial class Blade : Container
    {
        private readonly Box fill;
        private readonly SpriteText label;

        public Blade(float textSize)
        {
            AutoSizeAxes = Axes.Both;
            Anchor = Anchor.CentreLeft;
            Origin = Anchor.CentreLeft;

            Children = new Drawable[]
            {
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = textSize * 0.22f,
                    Shear = UserInterfaceConstant.Shear,
                    Child = fill = new Box
                    {
                        RelativeSizeAxes = Axes.Both
                    }
                },
                label = new SpriteText
                {
                    // Wide enough that the letters clear the slanted ends.
                    Margin = new MarginPadding { Left = textSize * 0.55f, Right = textSize * 0.55f, Top = textSize * 0.2f, Bottom = textSize * 0.2f },
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, (int)textSize, FontWeight.ExtraBold)
                }
            };
        }

        /// <summary>
        /// What the blade says.
        /// </summary>
        public string Text => label.Text;

        public void Apply(Piece piece)
        {
            label.Text = piece.Label;
            label.Color = piece.Ink;
            fill.Color = piece.Fill;
        }
    }
}
