// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Database;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// One difficulty in the list: a row of the rail (a steep tab in the tier colour carrying the level) or a
/// tile of the strip (a tier-coloured square with the level in large type). As with songs, the focus window
/// shows the focused one.
/// </summary>
public partial class BeatmapCard : CarouselCard<Beatmap>
{
    private const float row_tab_width = 112;
    private const float tile_square_size = 120;

    /// <summary>
    /// How much of a row's tab shows: a wash of the tier, so the rows don't shout over the focus window,
    /// the one solid thing in the list. A little more under the pointer.
    /// </summary>
    private const float row_tab_alpha = 0.4f;

    private const float row_tab_hover_alpha = 0.6f;

    public Beatmap Beatmap { get; private set; } = null!;

    [Resolved(canBeNull: true)]
    private ScoreStore? scoreStore { get; set; }

    [Resolved(canBeNull: true)]
    private ScreenPalette? screenPalette { get; set; }

    [Resolved(canBeNull: true)]
    private IYuukiColorProvider? colorProvider { get; set; }

    private readonly Container rowFace;
    private readonly Box rowHover;
    private readonly Box rowTab;
    private readonly SpriteText rowLevel;
    private readonly SpriteText rowName;
    private readonly SpriteText rowSub;
    private readonly FlowContainer rowText;
    private readonly FlowContainer rowExtras;
    private readonly LaneLabel rowLanes;
    private readonly BestMark rowBest;
    private readonly Container rowRuleHolder;
    private readonly Box rowRule;

    private readonly Container tileFace;
    private readonly Container tileSquare;
    private readonly Box tileFill;
    private readonly SpriteText tileTier;
    private readonly SpriteText tileLevel;
    private readonly SpriteText tileName;
    private readonly SpriteText tileSub;

    private bool vertical;

    public BeatmapCard()
    {
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;

        Add(rowFace = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 14,
            Alpha = 0,
            Children = new Drawable[]
            {
                rowHover = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0
                },
                // The tab leans at the game's steep angle; the row's rounded mask cuts its left end flat.
                rowTab = new Box
                {
                    // Shear pivots on the origin, so a centre-left origin leans it about its middle.
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    // Its middle ends at the level column's edge; leaning, its top reaches further (see Update).
                    RelativeSizeAxes = Axes.Y,
                    X = -60,
                    Width = row_tab_width + 60,
                    Shear = UserInterfaceConstant.Shear,
                    Alpha = row_tab_alpha
                },
                new Container
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = row_tab_width,
                    Child = rowLevel = new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        X = -14,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 34, FontWeight.ExtraBold)
                    }
                },
                rowText = new FlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = row_tab_width + 22,
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 2),
                    Children = new Drawable[]
                    {
                        rowName = new SpriteText
                        {
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 23, FontWeight.Bold),
                            Truncate = true
                        },
                        rowSub = new SpriteText
                        {
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.SemiBold),
                            Truncate = true
                        }
                    }
                },
                rowExtras = new FlowContainer
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    X = -16,
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Horizontal,
                    Spacing = new Vector2(12, 0),
                    Children = new Drawable[]
                    {
                        rowLanes = new LaneLabel
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft
                        },
                        rowBest = new BestMark
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft
                        }
                    }
                },
                rowRuleHolder = new Container
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    RelativeSizeAxes = Axes.X,
                    Height = 1,
                    Padding = new MarginPadding { Left = row_tab_width + 12, Right = 12 },
                    Child = rowRule = new Box { RelativeSizeAxes = Axes.Both }
                }
            }
        });

        Add(tileFace = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                tileSquare = new Container
                {
                    X = 6,
                    Size = new Vector2(tile_square_size),
                    Masking = true,
                    CornerRadius = 10,
                    Children = new Drawable[]
                    {
                        tileFill = new Box { RelativeSizeAxes = Axes.Both },
                        // A light stripe at the blade angle across the square's right edge.
                        new Box
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            RelativeSizeAxes = Axes.Y,
                            X = 26,
                            Width = 34,
                            Shear = UserInterfaceConstant.Shear,
                            Color = Color.White.WithAlpha(0.28f)
                        },
                        tileTier = new SpriteText
                        {
                            Position = new Vector2(10, 9),
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 11, FontWeight.ExtraBold)
                        },
                        tileLevel = new SpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Y = 6,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 44, FontWeight.ExtraBold)
                        }
                    }
                },
                tileName = new SpriteText
                {
                    X = 6,
                    Y = tile_square_size + 8,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 15, FontWeight.Bold),
                    Truncate = true,
                    MaxWidth = tile_square_size + 10
                },
                tileSub = new SpriteText
                {
                    X = 6,
                    Y = tile_square_size + 28,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 12, FontWeight.SemiBold),
                    Truncate = true,
                    MaxWidth = tile_square_size + 10
                }
            }
        });
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var ink = ScreenPalette.Resolve(screenPalette, colorProvider).Text;

        rowHover.Color = ink;
        rowName.Color = ink;
        rowSub.Color = SetlistColors.Secondary(ink);
        rowLanes.Color = ink;
        rowRule.Color = ink.WithAlpha(0.1f);
        tileName.Color = ink;
        tileSub.Color = SetlistColors.Secondary(ink);
    }

    public override void Apply(Beatmap beatmap, int index)
    {
        Beatmap = beatmap;

        var tier = BeatmapUtility.ColorForDifficulty(beatmap.DifficultyRating);
        var tierInk = SetlistColors.TierInk(tier);
        string tierName = BeatmapUtility.CalculateDifficultyLevel(beatmap).ToString();
        string level = BeatmapUtility.FormatLevel(beatmap.DifficultyRating);
        string name = string.IsNullOrEmpty(beatmap.DifficultyName) ? "Difficulty" : beatmap.DifficultyName;
        string lanes = beatmap.LaneCount > 0 ? BeatmapUtility.FormatLanes(beatmap.LaneCount) : "";

        rowTab.Color = tier;
        rowLevel.Color = tierInk;
        rowLevel.Text = level;
        rowName.Text = name;
        rowSub.Text = string.IsNullOrEmpty(beatmap.Creator) ? tierName : $"{tierName} · mapped by {beatmap.Creator}";
        rowLanes.Text = lanes;
        rowBest.SetBest(scoreStore?.GetBestMark(beatmap));

        tileFill.Color = tier;
        tileTier.Color = tierInk;
        tileTier.Text = tierName.ToUpperInvariant();
        tileLevel.Color = tierInk;
        tileLevel.Text = level;
        tileName.Text = name;
        tileSub.Text = string.IsNullOrEmpty(beatmap.Creator) ? lanes : $"{lanes} · {beatmap.Creator}";
    }

    /// <summary>
    /// The lane count on show ("4K"), empty when the difficulty has none.
    /// </summary>
    public string LaneText => rowLanes.Text;

    protected override void OnOrientationChanged(CarouselOrientation orientation)
    {
        vertical = orientation == CarouselOrientation.Vertical;
        rowFace.Alpha = vertical ? 1 : 0;
        tileFace.Alpha = vertical ? 0 : 1;
    }

    public override void Update()
    {
        base.Update();

        if (vertical)
        {
            // The text starts clear of the tab's top corner, which leans out past its middle by a third of
            // the row's height; the title sits up there, and used to run into it.
            float textX = row_tab_width + Math.Abs(UserInterfaceConstant.Shear.X) * rowFace.DrawHeight / 2 + 14;

            if (Math.Abs(rowText.X - textX) > 0.5f)
            {
                rowText.X = textX;
                rowRuleHolder.Padding = new MarginPadding { Left = textX - 10, Right = 12 };
            }

            float budget = Math.Max(0, rowFace.DrawWidth - textX - rowExtras.DrawWidth - 32);
            rowName.MaxWidth = budget;
            rowSub.MaxWidth = budget;
        }
    }

    public override bool OnHover(MouseEvent e)
    {
        rowHover.FadeTo(0.06f, 150, Easing.OutQuint);
        rowTab.FadeTo(row_tab_hover_alpha, 150, Easing.OutQuint);
        tileSquare.BorderThickness = 3;
        tileSquare.BorderColor = rowName.Color.WithAlpha(0.3f);
        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        rowHover.FadeOut(300, Easing.OutQuint);
        rowTab.FadeTo(row_tab_alpha, 300, Easing.OutQuint);
        tileSquare.BorderThickness = 0;
        return base.OnHoverLost(e);
    }
}
