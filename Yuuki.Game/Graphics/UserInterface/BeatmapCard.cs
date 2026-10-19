// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class BeatmapCard : CarouselCard<Beatmap>
{
    public const float CARD_SIZE = 118f;

    public Beatmap Beatmap { get; private set; }

    private Box backgroundBox;
    private Box selectedOverlay;
    private bool vertical;

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    // Coverflow (square) face: name over rating, centred, the lane count in the corner.
    private Container squareFace;
    private SpriteText nameText;
    private SpriteText ratingText;
    private LaneCountBadge squareLanes;

    // Bar (list) face: dark star-rating and lane-count badges, then the difficulty name over the mapper credit.
    private Container barFace;
    private SpriteText barNameText;
    private SpriteText barMapperText;
    private SpriteText barRatingText;
    private LaneCountBadge barLanes;

    public BeatmapCard()
    {
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;
        Masking = true;
        CornerRadius = 12;
        EdgeEffect = SHADOW_EDGE;

        Add(backgroundBox = new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        });

        // Lightens the selected difficulty's bar, as the selected set's is (see BeatmapSetCarouselCard).
        Add(selectedOverlay = new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Color = Color.White,
            Alpha = 0
        });

        Add(squareFace = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new FlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                    Children = new Drawable[]
                    {
                        nameText = new SpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 16, FontWeight.Bold)
                        },
                        ratingText = new SpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 14, FontWeight.Regular)
                        }
                    }
                },
                squareLanes = new LaneCountBadge(11)
                {
                    Position = new Vector2(8)
                }
            }
        });

        Add(barFace = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Alpha = 0,
            Padding = new MarginPadding { Left = 14, Right = 18 },
            Child = new FlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(12, 0),
                Children = new Drawable[]
                {
                    // Dark rounded star-rating badge
                    new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        AutoSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 8,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Color = Color.Black.WithAlpha(0.55f)
                            },
                            new FlowContainer
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                AutoSizeAxes = Axes.Both,
                                Direction = FlowDirection.Horizontal,
                                Spacing = new Vector2(4, 0),
                                Padding = new MarginPadding { Left = 10, Right = 12, Top = 5, Bottom = 5 },
                                Children = new Drawable[]
                                {
                                    new IconSprite
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Icon = IconUsage.Star,
                                        IconSize = 14,
                                        Color = Color.White
                                    },
                                    barRatingText = new SpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Bold),
                                        Color = Color.White
                                    }
                                }
                            }
                        }
                    },
                    barLanes = new LaneCountBadge(14)
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Margin = new MarginPadding { Left = -6 }
                    },
                    new FlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        AutoSizeAxes = Axes.Both,
                        Direction = FlowDirection.Vertical,
                        Spacing = new Vector2(0, 1),
                        Children = new Drawable[]
                        {
                            barNameText = new SpriteText
                            {
                                Anchor = Anchor.TopLeft,
                                Origin = Anchor.TopLeft,
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 18, FontWeight.Bold)
                            },
                            barMapperText = new SpriteText
                            {
                                Anchor = Anchor.TopLeft,
                                Origin = Anchor.TopLeft,
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 12, FontWeight.Regular)
                            }
                        }
                    }
                }
            }
        });
    }

    public override void Apply(Beatmap beatmap, int index)
    {
        Beatmap = beatmap;

        Color color = BeatmapUtility.ColorForDifficulty(beatmap.DifficultyRating);
        Color textColor = color.Darken(0.6f);

        backgroundBox.Color = color;

        string name = string.IsNullOrEmpty(beatmap.DifficultyName) ? "Difficulty" : beatmap.DifficultyName;
        string rating = $"{beatmap.DifficultyRating:0.0}";

        nameText.Text = name;
        nameText.Color = textColor;
        ratingText.Text = rating;
        ratingText.Color = textColor;

        barNameText.Text = name;
        barNameText.Color = textColor;
        barMapperText.Text = string.IsNullOrEmpty(beatmap.Creator) ? "" : $"mapped by {beatmap.Creator}";
        barMapperText.Color = textColor.WithAlpha(0.85f);
        barRatingText.Text = rating;

        string lanes = beatmap.LaneCount > 0 ? BeatmapUtility.FormatLanes(beatmap.LaneCount) : "";
        squareLanes.Text = lanes;
        barLanes.Text = lanes;
    }

    /// <summary>
    /// The lane count on show ("4K"), empty when the difficulty has none.
    /// </summary>
    public string LaneText => barLanes.Text;

    protected override void OnOrientationChanged(CarouselOrientation orientation)
    {
        vertical = orientation == CarouselOrientation.Vertical;
        squareFace.Alpha = vertical ? 0f : 1f;
        barFace.Alpha = vertical ? 1f : 0f;
    }

    /// <summary>
    /// Whether this is the picked difficulty (wearing the selection ring, in the list).
    /// </summary>
    public bool IsSelectedDifficulty => vertical && Beatmap != null && workingBeatmap?.Beatmap != null && Equals(workingBeatmap.Beatmap, Beatmap);

    public override void Update()
    {
        base.Update();

        // The picked difficulty wears the same ring and lift as the picked set. In the cover flow the
        // middle card is the selection, so there's none there, as for sets.
        bool selected = IsSelectedDifficulty;

        selectedOverlay.Alpha = selected ? 0.2f : 0f;
        EdgeEffect = selected ? SELECTED_EDGE : SHADOW_EDGE;
    }
}
