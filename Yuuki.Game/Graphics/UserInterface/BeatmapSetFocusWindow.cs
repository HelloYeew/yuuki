// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// The song list's focus window. In the rail it's a wide card: the cover, the title, artist and details,
/// and your best mark, skyline and lanes. In the strip it's a hero tile with only the cover and the
/// skyline, because the info panel right above it already names the song.
/// </summary>
public partial class BeatmapSetFocusWindow : CarouselFocusWindow<BeatmapSet>
{
    [Resolved(canBeNull: true)]
    private BeatmapsCollection? beatmapsCollection { get; set; }

    [Resolved(canBeNull: true)]
    private CoverColorStore? coverColors { get; set; }

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    private int seenColorVersion;

    protected override CarouselFocusFace<BeatmapSet> CreateFace() => new Face();

    protected override bool FlashesOnBeat(BeatmapSet set) => workingBeatmap?.BeatmapSet != null && Equals(workingBeatmap.BeatmapSet, set);

    protected override Color FillFor(BeatmapSet set) => SetlistColors.ForSong(set, difficultiesOf(set, beatmapsCollection), coverColors).Fill;

    public override void Update()
    {
        base.Update();

        // A song is shown in its tier colour until its cover has been decoded somewhere (usually its row,
        // a moment earlier) and its own colour picked.
        if (coverColors == null || coverColors.Version == seenColorVersion)
            return;

        seenColorVersion = coverColors.Version;
        RefreshColors();
    }

    private static IReadOnlyList<Beatmap> difficultiesOf(BeatmapSet set, BeatmapsCollection? collection) =>
        collection?.GetBeatmapsFromBeatmapSet(set) ?? Array.Empty<Beatmap>();

    private partial class Face : CarouselFocusFace<BeatmapSet>
    {
        private const float wide_cover_size = 116;
        private const float wide_text_x = wide_cover_size + 32;
        private const float hero_cover_size = 220;

        private static readonly Vector2 cover_decode_size = new Vector2(256, 256);

        [Resolved(canBeNull: true)]
        private Storage? storage { get; set; }

        [Resolved(canBeNull: true)]
        private YuukiConfigManager? config { get; set; }

        [Resolved(canBeNull: true)]
        private BeatmapsCollection? beatmapsCollection { get; set; }

        [Resolved(canBeNull: true)]
        private ScoreStore? scoreStore { get; set; }

        [Resolved(canBeNull: true)]
        private CoverColorStore? coverColors { get; set; }

        private readonly DelayedLoadCover cover;
        private readonly FlowContainer text;
        private readonly ScrollingText title;
        private readonly ScrollingText artist;
        private readonly IconStats details;
        private readonly FlowContainer extras;
        private readonly BestMark best;
        private readonly LevelSkyline skyline;
        private readonly LaneLabel lanes;

        private bool vertical;
        private Reactive<bool>? useUnicodeInfo;

        public Face()
        {
            Children = new Drawable[]
            {
                cover = new DelayedLoadCover
                {
                    // A cover fills its parent by default; this one is sized per orientation.
                    RelativeSizeAxes = Axes.None,
                    Masking = true,
                    CornerRadius = 12,
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Shadow,
                        Color = Color.Black.WithAlpha(0.22f),
                        Radius = 10,
                        Offset = new Vector2(0, 3)
                    },
                    DecodeTargetSize = cover_decode_size,
                    LoadInScale = 1.06f
                },
                text = new FlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = wide_text_x,
                    AutoSizeAxes = Axes.Y,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 1),
                    Children = new Drawable[]
                    {
                        title = new ScrollingText
                        {
                            RelativeSizeAxes = Axes.X,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 30, FontWeight.Bold)
                        },
                        artist = new ScrollingText
                        {
                            RelativeSizeAxes = Axes.X,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 18, FontWeight.SemiBold)
                        },
                        details = new IconStats(15)
                        {
                            Margin = new MarginPadding { Top = 6 }
                        }
                    }
                },
                extras = new FlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Horizontal,
                    Spacing = new Vector2(12, 0),
                    Children = new Drawable[]
                    {
                        best = new BestMark(13) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                        skyline = new LevelSkyline(30, 7) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                        lanes = new LaneLabel(15) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft }
                    }
                }
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            if (config == null)
                return;

            useUnicodeInfo = config.Get<bool>(YuukiSetting.UseUnicodeInfo);
            BindValueChanged(useUnicodeInfo, _ => updateText());
        }

        public override void SetOrientation(CarouselOrientation orientation)
        {
            vertical = orientation == CarouselOrientation.Vertical;

            if (vertical)
            {
                cover.Anchor = cover.Origin = Anchor.CentreLeft;
                cover.Position = new Vector2(12, 0);
                cover.Size = new Vector2(wide_cover_size);
                text.Alpha = 1;
                extras.Anchor = extras.Origin = Anchor.CentreRight;
                extras.Position = new Vector2(-18, 0);
            }
            else
            {
                // The hero: the cover, and under it only the skyline, best mark and lanes.
                cover.Anchor = cover.Origin = Anchor.TopLeft;
                cover.Position = new Vector2(14);
                cover.Size = new Vector2(hero_cover_size);
                text.Alpha = 0;
                extras.Anchor = Anchor.TopCentre;
                extras.Origin = Anchor.Centre;
                extras.Position = new Vector2(0, 14 + hero_cover_size + 26);
            }
        }

        protected override void OnApply(BeatmapSet set)
        {
            var difficulties = beatmapsCollection?.GetBeatmapsFromBeatmapSet(set) ?? Array.Empty<Beatmap>();

            RefreshColors();

            updateText();
            details.SetItems(detailsOf(set, difficulties));
            skyline.SetDifficulties(difficulties);
            lanes.Text = BeatmapUtility.FormatLaneRange(difficulties);
            best.SetBest(scoreStore?.GetBestMark(set));

            cover.SetSource(set.Cover == null && string.IsNullOrEmpty(set.CoverStoragePath) ? null : new BeatmapSetCoverSource(set, storage));
        }

        public override void RefreshColors()
        {
            if (Model == null)
                return;

            var difficulties = beatmapsCollection?.GetBeatmapsFromBeatmapSet(Model) ?? Array.Empty<Beatmap>();
            var (fill, ink) = SetlistColors.ForSong(Model, difficulties, coverColors);

            title.Color = ink;
            artist.Color = ink.WithAlpha(0.8f);
            details.Color = ink;
            lanes.Color = ink;
            cover.PlaceholderColor = fill.Darken(0.15f);
        }

        /// <summary>
        /// The level range, length and BPM. Not the lanes: the lane label beside the skyline has them.
        /// </summary>
        private static (IconUsage, string)[] detailsOf(BeatmapSet set, IReadOnlyList<Beatmap> difficulties) => new[]
        {
            (IconUsage.Rocket, BeatmapUtility.FormatLevelRange(difficulties)),
            (IconUsage.Timer, set.TotalLength > 0 ? TimeSpan.FromMilliseconds(set.TotalLength).ToString(@"m\:ss") : ""),
            (IconUsage.Metro, set.BPM > 0 ? $"{Math.Round(set.BPM)}" : "")
        };

        private void updateText()
        {
            if (Model == null)
                return;

            bool unicode = useUnicodeInfo?.Value ?? false;
            string displayTitle = Model.GetDisplayTitle(unicode);
            title.Text = string.IsNullOrEmpty(displayTitle) ? "Untitled" : displayTitle;
            artist.Text = Model.GetDisplayArtist(unicode);
        }

        public override void Update()
        {
            base.Update();

            if (!vertical)
                return;

            // The text column runs up to the extras, and long names scroll within it.
            text.Width = Math.Max(0, DrawWidth - wide_text_x - extras.DrawWidth - 36);
        }
    }
}
