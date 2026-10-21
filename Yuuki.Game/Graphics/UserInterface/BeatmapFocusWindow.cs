// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Database;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// The difficulty list's focus window, in the tier's colour. In the rail it's a wide card with the level on
/// a steep tab; in the strip it's a hero plate with the level in large type (the name and mapper are in the
/// info panel's difficulty row right above it).
/// </summary>
public partial class BeatmapFocusWindow : CarouselFocusWindow<Beatmap>
{
    protected override CarouselFocusFace<Beatmap> CreateFace() => new Face();

    protected override Color FillFor(Beatmap beatmap) => SetlistColors.TierFill(BeatmapUtility.ColorForDifficulty(beatmap.DifficultyRating));

    private partial class Face : CarouselFocusFace<Beatmap>
    {
        private const float wide_tab_width = 132;
        private const float hero_plate_size = 220;

        [Resolved(canBeNull: true)]
        private ScoreStore? scoreStore { get; set; }

        // wide card
        private readonly Container wide;
        private readonly Box wideTab;
        private readonly SpriteText wideLevel;
        private readonly SpriteText wideName;
        private readonly SpriteText wideSub;
        private readonly FlowContainer wideText;
        private readonly FlowContainer wideExtras;
        private readonly LaneLabel wideLanes;
        private readonly BestMark wideBest;

        // hero plate
        private readonly Container hero;
        private readonly Box heroFill;
        private readonly SpriteText heroTier;
        private readonly SpriteText heroLevel;
        private readonly LaneLabel heroLanes;
        private readonly BestMark heroBest;

        private bool vertical;

        public Face()
        {
            Children = new Drawable[]
            {
                wide = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        wideTab = new Box
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            // Its middle ends at the level column's edge; leaning, its top reaches further (see Update).
                            RelativeSizeAxes = Axes.Y,
                            X = -70,
                            Width = wide_tab_width + 70,
                            Shear = UserInterfaceConstant.Shear
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Y,
                            Width = wide_tab_width,
                            Child = wideLevel = new SpriteText
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                X = -16,
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 42, FontWeight.ExtraBold)
                            }
                        },
                        wideText = new FlowContainer
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            X = wide_tab_width + 26,
                            AutoSizeAxes = Axes.Both,
                            Direction = FlowDirection.Vertical,
                            Spacing = new Vector2(0, 2),
                            Children = new Drawable[]
                            {
                                wideName = new SpriteText
                                {
                                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 28, FontWeight.Bold),
                                    Truncate = true
                                },
                                wideSub = new SpriteText
                                {
                                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 18, FontWeight.SemiBold),
                                    Truncate = true
                                }
                            }
                        },
                        wideExtras = new FlowContainer
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            X = -18,
                            AutoSizeAxes = Axes.Both,
                            Direction = FlowDirection.Horizontal,
                            Spacing = new Vector2(12, 0),
                            Children = new Drawable[]
                            {
                                wideLanes = new LaneLabel(15) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                                wideBest = new BestMark(13) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft }
                            }
                        }
                    }
                },
                hero = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        new Container
                        {
                            Position = new Vector2(14),
                            Size = new Vector2(hero_plate_size),
                            Masking = true,
                            CornerRadius = 12,
                            Children = new Drawable[]
                            {
                                heroFill = new Box { RelativeSizeAxes = Axes.Both },
                                new Box
                                {
                                    Anchor = Anchor.CentreRight,
                                    Origin = Anchor.CentreRight,
                                    RelativeSizeAxes = Axes.Y,
                                    X = 34,
                                    Width = 76,
                                    Shear = UserInterfaceConstant.Shear,
                                    Color = Color.White.WithAlpha(0.28f)
                                },
                                heroTier = new SpriteText
                                {
                                    Position = new Vector2(14, 12),
                                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.ExtraBold)
                                },
                                heroLevel = new SpriteText
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Y = 8,
                                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 84, FontWeight.ExtraBold)
                                }
                            }
                        },
                        heroLanes = new LaneLabel(15)
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.CentreLeft,
                            Position = new Vector2(20, 14 + hero_plate_size + 26)
                        },
                        heroBest = new BestMark(13)
                        {
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.CentreRight,
                            Position = new Vector2(-20, 14 + hero_plate_size + 26)
                        }
                    }
                }
            };
        }

        public override void SetOrientation(CarouselOrientation orientation)
        {
            vertical = orientation == CarouselOrientation.Vertical;
            wide.Alpha = vertical ? 1 : 0;
            hero.Alpha = vertical ? 0 : 1;
        }

        protected override void OnApply(Beatmap beatmap)
        {
            var tier = BeatmapUtility.ColorForDifficulty(beatmap.DifficultyRating);
            var ink = SetlistColors.TierInk(tier);
            string tierName = BeatmapUtility.CalculateDifficultyLevel(beatmap).ToString();
            string level = BeatmapUtility.FormatLevel(beatmap.DifficultyRating);
            string lanes = beatmap.LaneCount > 0 ? BeatmapUtility.FormatLanes(beatmap.LaneCount) : "";
            var best = scoreStore?.GetBestMark(beatmap);

            wideTab.Color = tier;
            wideLevel.Color = ink;
            wideLevel.Text = level;
            wideName.Color = ink;
            wideName.Text = string.IsNullOrEmpty(beatmap.DifficultyName) ? "Difficulty" : beatmap.DifficultyName;
            wideSub.Color = ink.WithAlpha(0.8f);
            wideSub.Text = string.IsNullOrEmpty(beatmap.Creator) ? tierName : $"{tierName} · mapped by {beatmap.Creator}";
            wideLanes.Color = ink;
            wideLanes.Text = lanes;
            wideBest.SetBest(best);

            heroFill.Color = tier;
            heroTier.Color = ink;
            heroTier.Text = tierName.ToUpperInvariant();
            heroLevel.Color = ink;
            heroLevel.Text = level;
            heroLanes.Color = ink;
            heroLanes.Text = lanes;
            heroBest.SetBest(best);
        }

        public override void Update()
        {
            base.Update();

            if (!vertical)
                return;

            // The text starts clear of the tab's top corner, which leans out past its middle by a third of
            // the window's height; the name sits up there, and used to run into it.
            float textX = wide_tab_width + Math.Abs(UserInterfaceConstant.Shear.X) * DrawHeight / 2 + 16;

            if (Math.Abs(wideText.X - textX) > 0.5f)
                wideText.X = textX;

            float budget = Math.Max(0, DrawWidth - textX - wideExtras.DrawWidth - 36);
            wideName.MaxWidth = budget;
            wideSub.MaxWidth = budget;
        }
    }
}
