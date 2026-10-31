// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// The top of the results' More info page: the song played, small, and the run's figures at the right (its rank and
/// medal, score, accuracy, max combo and unstable rate), so the graphs under it read with what they add up to.
/// </summary>
public partial class MoreInfoHeader : Container
{
    public const float HEIGHT = 92;

    private const float cover_size = 64;

    // Where the song's text starts, and how far it stops short of the figures.
    private const float text_x = 14 + cover_size + 16;
    private const float text_gap = 32;
    private const float figures_x = 30;

    private readonly YuukiColorPalette palette;
    private readonly DelayedLoadCover cover;
    private readonly FlowContainer text;
    private readonly ScrollingText title;
    private readonly ScrollingText details;
    private readonly FlowContainer figures;

    // The song shown (its difficulty, and when it was played), and the one the cover was last pointed at.
    private Beatmap? beatmap;
    private BeatmapSet? set;
    private DateTimeOffset? played;
    private BeatmapSet? coverSet;

    // Titles and artists in their own script, as song select shows them (the Unicode setting).
    private bool useUnicode;

    [Resolved(canBeNull: true)]
    private Storage? storage { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    /// <summary>
    /// Whether the song's cover is showing (its placeholder until then).
    /// </summary>
    public bool HasCover => cover.HasCover;

    public MoreInfoHeader(YuukiColorPalette palette)
    {
        this.palette = palette;

        RelativeSizeAxes = Axes.X;
        Height = HEIGHT;
        Masking = true;
        CornerRadius = 12;

        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = palette.Surface,
                Alpha = 0.92f
            },
            cover = new DelayedLoadCover
            {
                // It fills its parent by default.
                RelativeSizeAxes = Axes.None,
                Position = new Vector2(14),
                Size = new Vector2(cover_size),
                Masking = true,
                CornerRadius = 10,
                DecodeTargetSize = new Vector2(128),
                PlaceholderColor = palette.Text.WithAlpha(0.12f)
            },
            // As wide as the room left beside the figures (see Update); what's longer scrolls, as song select's song does.
            text = new FlowContainer
            {
                Position = new Vector2(text_x, 17),
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Children = new Drawable[]
                {
                    title = new ScrollingText
                    {
                        RelativeSizeAxes = Axes.X,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 26, FontWeight.Bold),
                        Color = palette.Text
                    },
                    details = new ScrollingText
                    {
                        RelativeSizeAxes = Axes.X,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.SemiBold),
                        Color = ResultsInk.Secondary(palette)
                    }
                }
            },
            figures = new FlowContainer
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -figures_x,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(34, 0)
            }
        };
    }

    /// <summary>
    /// Shows the song played: its title, artist, difficulty and mapper, its cover, and when it was played (the footer that
    /// says so is hidden on a phone).
    /// </summary>
    public void ShowSong(Beatmap? beatmap, BeatmapSet? set, DateTimeOffset? played = null)
    {
        this.beatmap = beatmap;
        this.set = set ?? beatmap?.BeatmapSet;
        this.played = played;

        updateText();

        if (IsLoaded)
            updateCover();
    }

    private void updateText()
    {
        string shownTitle = set?.GetDisplayTitle(useUnicode) ?? "";
        title.Text = set == null ? "No song selected" : string.IsNullOrEmpty(shownTitle) ? "Untitled" : shownTitle;

        var parts = new List<string>();

        if (set?.GetDisplayArtist(useUnicode) is { Length: > 0 } artist)
            parts.Add(artist);

        if (beatmap != null)
        {
            parts.Add($"{beatmap.DifficultyName} {BeatmapUtility.FormatLevel(beatmap.DifficultyRating)}".Trim());

            if (!string.IsNullOrEmpty(beatmap.Creator))
                parts.Add($"mapped by {beatmap.Creator}");
        }

        if (played is DateTimeOffset date)
            parts.Add($"played {DateUtility.FormatPlayed(date)}");

        details.Text = string.Join(" · ", parts);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // Follows the setting live, as song select does, so changing it from the settings shows here at once. Tracked,
        // so the shared setting doesn't keep the header (and its screen) alive.
        if (config != null)
        {
            var setting = config.Get<bool>(YuukiSetting.UseUnicodeInfo);
            useUnicode = setting.Value;
            BindValueChanged(setting, e =>
            {
                useUnicode = e.NewValue;
                updateText();
            });
            updateText();
        }

        // Shown before it loaded (as the results screen does, from its own load), the cover waits for it: its storage
        // and the cover's texture store resolve on load, and a source set before then would be ignored.
        updateCover();
    }

    public override void Update()
    {
        // Before the frame's layout, so the text is the width this frame shows.
        text.Width = Math.Max(0, DrawWidth - text_x - figures.DrawWidth - figures_x - text_gap);

        base.Update();
    }

    /// <summary>
    /// The song's title as shown.
    /// </summary>
    public string Title => title.Text;

    /// <summary>
    /// The line under it: artist, difficulty, mapper and when it was played.
    /// </summary>
    public string Details => details.Text;

    private void updateCover()
    {
        if (set == coverSet)
            return;

        coverSet = set;
        cover.SetSource(set != null && storage != null && (set.Cover != null || !string.IsNullOrEmpty(set.CoverStoragePath))
            ? new BeatmapSetCoverSource(set, storage)
            : null);
    }

    /// <summary>
    /// Shows the run's figures; the unstable rate only once its judgements are in (null before).
    /// </summary>
    public void ShowRun(GameplayResult result, double? unstableRate)
    {
        var mark = new BestMark(17)
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft
        };
        mark.SetBest(ScoreInfo.From(result, 0, 0));

        figures.Children = new Drawable[]
        {
            mark,
            figure("Score", ((int)result.Score).ToString("D7")),
            figure("Accuracy", (result.Accuracy * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%"),
            figure("Max combo", $"{result.MaxCombo}x / {result.TotalNotes}"),
            figure("UR", unstableRate is double ur ? ur.ToString("0", CultureInfo.InvariantCulture) : "—")
        };
    }

    private Drawable figure(string label, string value) => new FlowContainer
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        AutoSizeAxes = Axes.Both,
        Direction = FlowDirection.Vertical,
        Spacing = new Vector2(0, 2),
        Children = new Drawable[]
        {
            new SpriteText
            {
                Text = label.ToUpperInvariant(),
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 12, FontWeight.Bold),
                Color = ResultsInk.Secondary(palette)
            },
            new SpriteText
            {
                Text = value,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 26, FontWeight.ExtraBold),
                Color = palette.Text
            }
        }
    };
}
