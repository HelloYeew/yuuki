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

        // Skimming, the rail's window is short: a small cover, the title and artist, and the best mark.
        private const float skim_wide_cover_size = 56;
        private const float skim_text_scale = 0.8f;
        private const float extras_gap = 12;

        // On the hero's cover while it skims, as a tile has it: in from its corner.
        private const float hero_best_inset = 8;

        // The hero shows the title and artist under its cover instead of the skyline and best mark.
        private const float hero_text_scale = 0.72f;

        private float skim;
        private bool phone;

        // A phone's window is a little shorter and its hero a little smaller (see BeatmapSetCarousel), and
        // the text in them a little bigger. Its rail is narrower, so the extras move down beside the
        // details line where they fit, leaving the title the whole width.
        private float wideCoverSize => phone ? 104 : wide_cover_size;
        private float heroCoverSize => phone ? 200 : hero_cover_size;
        private float skimCoverSize => phone ? 64 : skim_wide_cover_size;
        private float textGap => phone ? 28 : 32;
        private float skimTextScale => phone ? 0.82f : skim_text_scale;

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
                    Spacing = new Vector2(extras_gap, 0),
                    Children = new Drawable[]
                    {
                        skyline = new LevelSkyline(30, 7) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                        lanes = new LaneLabel(15) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft }
                    }
                },
                // Apart from the extras, as it stays when they step aside for a skim.
                best = new BestMark(13)
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
            applyOrientation();
        }

        public override void SetPhone(bool value)
        {
            if (phone == value)
                return;

            phone = value;

            title.Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, phone ? 31 : 30, FontWeight.Bold);
            artist.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, phone ? 19 : 18, FontWeight.SemiBold);

            // Sized when they were made, so scaled up as a whole: 16 for 15, as the phone's text is.
            details.Scale = new Vector2(phone ? 16 / 15f : 1);
            extras.Scale = new Vector2(phone ? 16 / 15f : 1);
            best.Scale = extras.Scale;

            applyOrientation();
        }

        private void applyOrientation()
        {
            if (vertical)
            {
                cover.Anchor = cover.Origin = Anchor.CentreLeft;
                cover.Position = new Vector2(12, 0);
                cover.Size = new Vector2(wideCoverSize);
                text.Alpha = 1;
                extras.Anchor = extras.Origin = Anchor.CentreRight;
                extras.Position = new Vector2(-18, 0);
                best.Anchor = best.Origin = Anchor.CentreRight;
            }
            else
            {
                // The hero: the cover, and under it only the skyline, best mark and lanes.
                cover.Anchor = cover.Origin = Anchor.TopLeft;
                cover.Position = new Vector2(14);
                cover.Size = new Vector2(heroCoverSize);
                text.Alpha = 0;
                extras.Anchor = Anchor.TopCentre;
                extras.Origin = Anchor.Centre;
                extras.Position = new Vector2(0, 14 + heroCoverSize + 26);
                best.Anchor = Anchor.TopLeft;
                best.Origin = Anchor.CentreLeft;
            }

            layOutForSkim();
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

        public override void SetSkim(float amount)
        {
            if (amount == skim)
                return;

            skim = amount;
            layOutForSkim();
        }

        /// <summary>
        /// While the list skims the title, artist and best mark stay and the rest steps aside: in the rail the
        /// cover shrinks with the window, and the hero swaps its skyline and lanes for the title and artist.
        /// </summary>
        private void layOutForSkim()
        {
            float lerp(float rest, float skimming) => rest + (skimming - rest) * skim;

            details.Alpha = vertical ? 1 - skim : 0;
            extras.Alpha = 1 - skim;

            if (vertical)
            {
                float coverSize = lerp(wideCoverSize, skimCoverSize);

                cover.Size = new Vector2(coverSize);
                text.Anchor = text.Origin = Anchor.CentreLeft;
                text.Position = new Vector2(coverSize + textGap, 0);
                text.Scale = new Vector2(lerp(1, skimTextScale));
                text.Alpha = 1;
            }
            else
            {
                text.Anchor = text.Origin = Anchor.TopLeft;
                text.Position = new Vector2(14, 14 + heroCoverSize + 6);
                text.Scale = new Vector2(hero_text_scale);
                text.Alpha = skim;
            }
        }

        public override void Update()
        {
            base.Update();

            float gap = extras_gap * extras.Scale.X;
            float bestWidth = best.Alpha > 0 ? (best.DrawWidth + best.Margin.Left + best.Margin.Right) * best.Scale.X : 0;

            if (!vertical)
            {
                // Across the hero, under its cover.
                text.Width = Math.Max(0, (DrawWidth - 28) / text.Scale.X);

                // Beside the skyline and lanes, the three centered on the hero. Skimming, the text takes their
                // place, and it moves up onto the foot of the cover.
                float besideExtras = bestWidth > 0 ? bestWidth + gap : 0;
                float across = extras.DrawWidth * extras.Scale.X + besideExtras;
                float restY = extras.Y;
                float skimY = 14 + heroCoverSize - hero_best_inset - best.DrawHeight / 2;

                extras.X = besideExtras / 2;
                best.X = (DrawWidth - across) / 2 + (14 + hero_best_inset - (DrawWidth - across) / 2) * skim;
                best.Y = restY + (skimY - restY) * skim;
                return;
            }

            float besideBest = bestWidth > 0 && extras.DrawWidth > 0 ? extras.DrawWidth * extras.Scale.X + gap : extras.DrawWidth * extras.Scale.X;
            float extrasWidth = besideBest + bestWidth;
            float fullRoom = DrawWidth - text.X - 18;

            // On a phone the extras go beside the details line when both fit there, centred on it (the last
            // line of the text column); otherwise beside the whole text, centred on the window.
            bool onDetailsLine = phone && details.DrawWidth * details.Scale.X * text.Scale.X + 16 + extrasWidth <= fullRoom;
            extras.Y = onDetailsLine ? (text.DrawHeight - details.DrawHeight * details.Scale.Y) / 2 * text.Scale.Y : 0;

            // Over at the edge the extras leave as they step aside, and back in the middle of the window.
            best.X = extras.X - besideBest * (1 - skim);
            best.Y = extras.Y * (1 - skim);

            float room = onDetailsLine ? fullRoom - bestWidth * skim : fullRoom - besideBest * (1 - skim) - bestWidth - 18;

            // The text column runs up to the extras (or across the window), and long names scroll within it.
            text.Width = Math.Max(0, room / text.Scale.X);
        }
    }
}
