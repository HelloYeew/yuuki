// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// The run by kind of note: how many of each were hit, as a bar and a count, and the holds let go of early. It answers
/// "holds or swipes?".
/// </summary>
public partial class NoteTypeBars : FlowContainer
{
    private const float row_height = 18;
    private const float name_width = 78;
    private const float count_width = 70;

    private readonly YuukiColorPalette palette;

    public NoteTypeBars(YuukiColorPalette palette)
    {
        this.palette = palette;

        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Direction = FlowDirection.Vertical;
        Spacing = new Vector2(0, 3);
    }

    public void SetRun(PlayAnalysis run)
    {
        var rows = new List<Drawable>();

        foreach (NoteTypeFigures type in run.NoteTypes)
            rows.Add(row(type.Name, type.Total > 0 ? (float)type.Hit / type.Total : 0, $"{type.Hit}/{type.Total}"));

        if (run.Result.HoldBreakCount > 0)
            rows.Add(breaks(run.Result.HoldBreakCount));

        Children = rows;
    }

    private Drawable row(string name, float share, string count) => new Container
    {
        RelativeSizeAxes = Axes.X,
        Height = row_height,
        Children = new Drawable[]
        {
            text(name, palette.Text, Anchor.CentreLeft),
            new Container
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                RelativeSizeAxes = Axes.X,
                Height = 8,
                X = name_width + 12,
                Padding = new MarginPadding { Right = name_width + 12 + count_width + 12 },
                Child = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 4,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Color = palette.Text.WithAlpha(0.12f)
                        },
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = share,
                            Color = palette.Control.WithAlpha(0.75f)
                        }
                    }
                }
            },
            text(count, palette.Text, Anchor.CentreRight)
        }
    };

    private Drawable breaks(int count) => new Container
    {
        RelativeSizeAxes = Axes.X,
        Height = row_height,
        Children = new Drawable[]
        {
            text("Breaks", JudgementColors.MISS, Anchor.CentreLeft),
            new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = name_width + 12,
                Text = "holds let go early",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 14, FontWeight.SemiBold),
                Color = ResultsInk.Secondary(palette)
            },
            text(count.ToString(), JudgementColors.MISS, Anchor.CentreRight)
        }
    };

    private static SpriteText text(string value, Sakura.Framework.Graphics.Colors.Color color, Anchor anchor) => new SpriteText
    {
        Anchor = anchor,
        Origin = anchor,
        Text = value,
        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 14, FontWeight.Bold),
        Color = color
    };
}
