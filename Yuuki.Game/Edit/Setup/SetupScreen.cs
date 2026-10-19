// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Audio;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Platform.Dialogs;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Beatmaps.Difficulty;
using Yuuki.Game.Edit.Components;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Edit.Setup;

/// <summary>
/// The Setup tab
/// </summary>
public partial class SetupScreen : Container
{
    private static readonly string[] video_extensions = { "mp4", "mov", "avi", "webm", "mkv" };

    private readonly EditorScreen editor;

    [Resolved(canBeNull: true)]
    private AppHost? host { get; set; }

    [Resolved(canBeNull: true)]
    private Storage? storage { get; set; }

    [Resolved(canBeNull: true)]
    private IYuukiColorProvider? colorProvider { get; set; }

    private SpriteText trackName = null!;
    private SpriteText coverName = null!;
    private SpriteText backgroundName = null!;
    private SpriteText videoName = null!;
    private EditorButton removeVideo = null!;
    private DelayedLoadCover coverPreview = null!;
    private DelayedLoadCover backgroundPreview = null!;
    private VideoPreview videoPreview = null!;
    private WaveformGraph songPreview = null!;
    private string? shownCover;
    private string? shownBackground;

    private SpriteText info = null!;
    private bool infoDirty = true;
    private double nextInfoUpdate;

    /// <param name="editor">What picked files are handed to.</param>
    public SetupScreen(EditorScreen editor)
    {
        this.editor = editor;
    }

    // Two columns side by side: the set's files on the left, its words on the right.
    private const float form_width = 620;
    private const float column_gap = 20;
    private const float side_margin = 12;
    private const float label_width = 150;

    [Resolved]
    private EditorSession session { get; set; } = null!;

    private Container columns = null!;
    private FlowContainer filesColumn = null!;
    private FlowContainer wordsColumn = null!;

    // Each field and how to read its value from the beatmap, to show it whenever it isn't being typed in.
    private readonly List<(EditorTextBox Box, Func<Beatmap, string> Read)> fields = new List<(EditorTextBox, Func<Beatmap, string>)>();

    private readonly Dictionary<int, EditorButton> laneButtons = new Dictionary<int, EditorButton>();
    private SpriteText laneHint = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Add(columns = new Container
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Width = form_width * 2 + column_gap,
            Children = new Drawable[]
            {
                column(out filesColumn, 0),
                column(out wordsColumn, form_width + column_gap)
            }
        });

        var resources = new EditorPanel("Resources");
        resources.AddRow(mediaRow("Album cover", new Vector2(96), coverPreview = new DelayedLoadCover { Size = Vector2.One, FadeDuration = 200 }, out coverName,
            () => pick("Choose the album cover", "Images", BeatmapCreator.IMAGE_EXTENSIONS, path => editor.ChangeCover(path))));
        resources.AddRow(mediaRow("Background", new Vector2(171, 96), backgroundPreview = new DelayedLoadCover { Size = Vector2.One, FadeDuration = 200 }, out backgroundName,
            () => pick("Choose a background", "Images", BeatmapCreator.IMAGE_EXTENSIONS, path => editor.ChangeBackground(path))));

        var videoRow = mediaRow("Video", new Vector2(171, 96), videoPreview = new VideoPreview { RelativeSizeAxes = Axes.Both, Size = Vector2.One }, out videoName,
            () => pick("Choose a video", "Videos", video_extensions, path => editor.ChangeVideo(path)));
        videoRow.Add(removeVideo = new EditorButton(IconUsage.Delete, "Remove", 14)
        {
            X = label_width + 171 + 14 + 126,
            Y = 96 - 34,
            Size = new Vector2(110, 34),
            Action = editor.RemoveVideo
        });
        resources.AddRow(videoRow);
        resources.AddRow(mediaRow("Song", new Vector2(171, 96), new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One, Color = Color.Black.WithAlpha(0.35f) },
                songPreview = new WaveformGraph
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = Vector2.One,
                    Resolution = 0.5f,
                    Waveform = editor.CurrentWaveform
                }
            }
        }, out trackName, () => pick("Choose the song", "Audio", BeatmapCreator.AUDIO_EXTENSIONS, path => editor.ChangeTrack(path))));

        // In the timeline's colours.
        var colors = EditorColors.From(colorProvider);
        songPreview.Color = colors.Accent.WithAlpha(0.6f);
        songPreview.LowColor = ColorExtensions.FromHex("ddffff");
        songPreview.MidColor = colors.Accent;
        songPreview.HighColor = colors.AccentStrong;
        resources.AddRow(note("The video plays behind the menus and gameplay, in time with the song."));
        filesColumn.Add(resources);

        var about = new EditorPanel("Worked out by the game");
        about.AddRow(info = new SpriteText
        {
            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Bold),
            Color = Color.White
        });
        about.AddRow(note("Length comes from the song, tempo from the timing, the rating from the notes (saved with it)."));
        filesColumn.Add(about);

        var song = new EditorPanel("Song");
        song.AddRow(field("Artist", b => b.BeatmapSet.Artist, (b, v) => b.BeatmapSet.Artist = v));
        song.AddRow(field("Artist (original)", b => b.BeatmapSet.ArtistUnicode, (b, v) => b.BeatmapSet.ArtistUnicode = v));
        song.AddRow(field("Title", b => b.BeatmapSet.Title, (b, v) => b.BeatmapSet.Title = v));
        song.AddRow(field("Title (original)", b => b.BeatmapSet.TitleUnicode, (b, v) => b.BeatmapSet.TitleUnicode = v));
        song.AddRow(field("Source", b => b.BeatmapSet.Source, (b, v) => b.BeatmapSet.Source = v));
        song.AddRow(field("Source (original)", b => b.BeatmapSet.SourceUnicode, (b, v) => b.BeatmapSet.SourceUnicode = v));
        song.AddRow(field("Genre", b => b.BeatmapSet.Genre, (b, v) => b.BeatmapSet.Genre = v));
        song.AddRow(field("Set creator", b => b.BeatmapSet.Creator, (b, v) => b.BeatmapSet.Creator = v));
        wordsColumn.Add(song);

        var difficulty = new EditorPanel("Difficulty");
        difficulty.AddRow(field("Difficulty name", b => b.DifficultyName, (b, v) => b.DifficultyName = v));
        difficulty.AddRow(field("Mapper", b => b.Creator, (b, v) => b.Creator = v));

        var lanes = new Container { Height = 34 };
        lanes.Add(label("Lanes"));

        for (int count = LaneKeyLayouts.MIN_LANES; count <= LaneKeyLayouts.MAX_LANES; count++)
        {
            int laneCount = count;

            var button = new EditorButton(text: laneCount.ToString(CultureInfo.InvariantCulture))
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = label_width + (laneCount - LaneKeyLayouts.MIN_LANES) * 50,
                Size = new Vector2(44, 34),
                Action = () => setLaneCount(laneCount)
            };

            laneButtons[laneCount] = button;
            lanes.Add(button);
        }

        difficulty.AddRow(lanes);
        difficulty.AddRow(laneHint = new SpriteText
        {
            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.Bold),
            Color = Color.White.WithAlpha(0.5f)
        });
        wordsColumn.Add(difficulty);

        var preview = new EditorPanel("Preview");
        var previewRow = field("Preview time (ms)", b => b.BeatmapSet.PreviewTime.ToString(CultureInfo.InvariantCulture), (b, v) =>
        {
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int time) && time >= 0)
                b.BeatmapSet.PreviewTime = time;
        }, 160);

        previewRow.Add(new EditorButton(IconUsage.AccessTime, "Now", 14)
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            X = label_width + 166,
            Size = new Vector2(90, 34),
            Action = () => session.Beatmap.UpdateMetadata(b => b.BeatmapSet.PreviewTime = (int)session.Clock.CurrentTime)
        });

        preview.AddRow(previewRow);
        preview.AddRow(new SpriteText
        {
            Text = "Where song select starts playing the song. Shown in green along the bottom.",
            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.Bold),
            Color = Color.White.WithAlpha(0.5f)
        });
        wordsColumn.Add(preview);

        session.Beatmap.MetadataChanged += updateLanes;
        session.Beatmap.NotesChanged += updateLanes;
        session.Beatmap.MetadataChanged += invalidateInfo;
        session.Beatmap.NotesChanged += invalidateInfo;
        session.Beatmap.TimingChanged += invalidateInfo;
        updateLanes();
    }

    /// <summary>
    /// One of the two columns, scrolling on its own.
    /// </summary>
    private static YuukiScrollableContainer column(out FlowContainer flow, float x) => new YuukiScrollableContainer
    {
        X = x,
        RelativeSizeAxes = Axes.Y,
        Height = 1,
        Width = form_width,
        Child = flow = new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            Width = 1,
            AutoSizeAxes = Axes.Y,
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, 12),
            Padding = new MarginPadding { Bottom = 20 }
        }
    };

    /// <summary>
    /// An image or video the set uses: a preview of it, its name and a button to pick another.
    /// </summary>
    private static Container mediaRow(string text, Vector2 previewSize, Drawable preview, out SpriteText name, Action choose)
    {
        name = new SpriteText
        {
            X = label_width + previewSize.X + 14,
            Y = 8,
            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Bold),
            Color = Color.White
        };

        return new Container
        {
            Height = previewSize.Y,
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Y = 8,
                    Text = text,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 14, FontWeight.Bold),
                    Color = Color.White.WithAlpha(0.7f)
                },
                new Container
                {
                    X = label_width,
                    Size = previewSize,
                    Masking = true,
                    CornerRadius = 8,
                    Child = preview
                },
                name,
                new EditorButton(IconUsage.FolderOpen, "Choose…", 14)
                {
                    X = label_width + previewSize.X + 14,
                    Y = previewSize.Y - 34,
                    Size = new Vector2(120, 34),
                    Action = choose
                }
            }
        };
    }

    private static SpriteText note(string text) => new SpriteText
    {
        Text = text,
        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.Bold),
        Color = Color.White.WithAlpha(0.5f)
    };

    // The system's file picker; its answer comes back on the update thread.
    private void pick(string title, string kind, string[] extensions, Action<string> use) => host?.ShowOpenFileDialog(new FileDialogOptions
    {
        Title = title,
        Filters = new[] { new FileDialogFilter(kind, extensions) }
    }, result =>
    {
        if (result.Successful && result.Path != null)
            use(result.Path);
    });

    private static SpriteText label(string text) => new SpriteText
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        Text = text,
        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 14, FontWeight.Bold),
        Color = Color.White.WithAlpha(0.7f)
    };

    /// <summary>
    /// A labelled text field editing part of the beatmap, each confirmed change one undo step.
    /// </summary>
    private Container field(string text, Func<Beatmap, string> read, Action<Beatmap, string> write, float width = form_width - label_width - 40)
    {
        var box = new EditorTextBox
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            X = label_width,
            Width = width
        };

        box.Committed += value => session.Beatmap.UpdateMetadata(b => write(b, value.Trim()));
        fields.Add((box, b => read(b) ?? string.Empty));

        return new Container
        {
            Height = 34,
            Children = new Drawable[] { label(text), box }
        };
    }

    // The fewest lanes the notes fit in: one more than the furthest lane any note starts or ends in.
    private int lanesInUse()
    {
        int used = 0;

        foreach (var note in session.Beatmap.Notes)
        {
            used = Math.Max(used, (int)note.Lane + 1);

            if (note.EndLane is NoteLane end)
                used = Math.Max(used, (int)end + 1);
        }

        return used;
    }

    private void setLaneCount(int count)
    {
        if (count < lanesInUse() || session.Beatmap.Beatmap.LaneCount == count)
            return;

        session.Beatmap.UpdateMetadata(b => b.LaneCount = count);
    }

    private void updateLanes()
    {
        int used = lanesInUse();
        int current = session.Beatmap.Beatmap.LaneCount;

        foreach (var (count, button) in laneButtons)
        {
            button.Active = count == current;
            button.Enabled = count >= used;
        }

        laneHint.Text = used > LaneKeyLayouts.MIN_LANES
            ? $"Notes reach lane {used}, so fewer lanes would leave some out. Move them first."
            : "Lanes can be added any time; taking them away needs them empty.";
    }

    public override void Update()
    {
        base.Update();

        // Both columns fit side by side, scaled down together when the window is too narrow for them.
        float scale = Math.Min(1, (DrawWidth - side_margin * 2) / columns.Width);
        columns.Scale = new Vector2(Math.Max(0.5f, scale));
        columns.Height = DrawHeight / columns.Scale.Y;

        var beatmap = session.Beatmap.Beatmap;

        foreach (var (box, read) in fields)
            box.SetText(read(beatmap));

        var set = beatmap.BeatmapSet;

        show(trackName, set.TrackPath);
        show(coverName, set.CoverPath);
        show(backgroundName, set.BackgroundPath);
        show(videoName, set.HasVideo ? set.VideoPath : null);
        removeVideo.Enabled = set.HasVideo;

        // The previews follow the files (each new file is its own image to load).
        if (set.CoverStoragePath != shownCover)
        {
            shownCover = set.CoverStoragePath;
            coverPreview.SetSource(storage == null || string.IsNullOrEmpty(shownCover) ? null : new BeatmapSetCoverSource(set, storage));
        }

        videoPreview.Active = Alpha > 0.01f;
        videoPreview.SetVideo(storage != null && set.HasVideo && !string.IsNullOrEmpty(set.VideoStoragePath) && storage.Exists(set.VideoStoragePath)
            ? storage.GetFullPath(set.VideoStoragePath)
            : null);

        if (set.BackgroundStoragePath != shownBackground)
        {
            shownBackground = set.BackgroundStoragePath;
            backgroundPreview.SetSource(string.IsNullOrEmpty(shownBackground) ? null : new BeatmapSetBackgroundSource(set, storage));
        }

        // The rating takes a moment to work out, so it's done when something changed, at most twice a second.
        if (infoDirty && Clock.CurrentTime >= nextInfoUpdate)
        {
            infoDirty = false;
            nextInfoUpdate = Clock.CurrentTime + 500;
            info.Text = describe();
        }
    }

    private static void show(SpriteText text, string? file)
    {
        string shown = string.IsNullOrEmpty(file) ? "None" : file;

        if (text.Text != shown)
            text.Text = shown;
    }

    private void invalidateInfo() => infoDirty = true;

    /// <summary>
    /// Shows the song's new waveform (the song was changed).
    /// </summary>
    public void ShowWaveform(Waveform? waveform) => songPreview.Waveform = waveform;

    // "3:42 long · 132 BPM · 2.85★ · set 123, difficulty 456"
    private string describe()
    {
        var beatmap = session.Beatmap;
        var set = beatmap.BeatmapSet;
        var timing = beatmap.ControlPoints;

        double lastNote = beatmap.Notes.Count > 0 ? beatmap.Notes.Max(n => n.EndTime) : 0;
        string tempo = timing.HasTiming ? $"{timing.GetMostCommonBpm(lastNote):0.##} BPM" : "no timing yet";
        // TODO: this too
        double rating = OsuManiaStarRating.Calculate(beatmap.Notes, beatmap.Beatmap.LaneCount);

        return $"{BeatmapSetUtility.GetFormattedTime(set)} long  ·  {tempo}  ·  {rating:0.00}★  ·  set {set.ID}, difficulty {beatmap.Beatmap.ID}";
    }

    protected override void Dispose(bool isDisposing)
    {
        session.Beatmap.MetadataChanged -= updateLanes;
        session.Beatmap.NotesChanged -= updateLanes;
        session.Beatmap.MetadataChanged -= invalidateInfo;
        session.Beatmap.NotesChanged -= invalidateInfo;
        session.Beatmap.TimingChanged -= invalidateInfo;
        base.Dispose(isDisposing);
    }
}
