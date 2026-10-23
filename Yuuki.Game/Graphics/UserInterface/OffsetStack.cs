// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// What the game plays with, one layer on another: what it measures itself, the audio device's audio offset and the
/// display's visual offset, and a song's own offset; then where that leaves the judging and the notes. As rows, or as
/// one line (<see cref="Compact"/>). Follows the devices and the offsets as they change.
/// </summary>
public partial class OffsetStack : FlowContainer
{
    // How often it's read again: the measured delays settle over seconds, and the offsets change by hand.
    private const double refresh_interval = 250;

    // The rows' columns: what, how much, for what. A little short of the whole, as a flow wraps what doesn't fit.
    private static readonly float[] columns = { 0.3f, 0.42f, 0.26f };

    private readonly YuukiColorPalette palette;

    /// <summary>
    /// Whether it's one line, for under the offsets on the setup screen before a song.
    /// </summary>
    public bool Compact { get; init; }

    /// <summary>
    /// The difficulty whose song offset counts, and its name; null to say what a song offset would add.
    /// </summary>
    public int? BeatmapId { get; init; }

    public string? DifficultyName { get; init; }

    /// <summary>
    /// The sound's and the picture's delays the game measures (ms). Set, they're taken as given instead (tests).
    /// </summary>
    public double? SoundDelay { get; set; }

    public double? PictureDelay { get; set; }

    /// <summary>
    /// What it says, a line a row (tests).
    /// </summary>
    public string Summary { get; private set; } = string.Empty;

    [Resolved(canBeNull: true)]
    private OffsetManager? offsets { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private IAudioManager? audioManager { get; set; }

    [Resolved(canBeNull: true)]
    private AppHost? host { get; set; }

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    private double nextRefresh;

    public OffsetStack(YuukiColorPalette palette)
    {
        this.palette = palette;

        AutoSizeAxes = Axes.Y;
        Direction = FlowDirection.Vertical;
        Spacing = new Vector2(0, 8);
    }

    public override void Update()
    {
        base.Update();

        if (Clock.CurrentTime < nextRefresh)
            return;

        nextRefresh = Clock.CurrentTime + refresh_interval;

        List<(string What, string Value, string For)> rows = layers();
        string summary = string.Join("\n", rows.ConvertAll(r => $"{r.What} | {r.Value} | {r.For}"));

        if (summary == Summary)
            return;

        Summary = summary;
        show(rows);
    }

    private List<(string What, string Value, string For)> layers()
    {
        int audio = config?.Get<int>(YuukiSetting.AudioOffset).Value ?? 0;
        int visual = config?.Get<int>(YuukiSetting.VisualOffset).Value ?? 0;
        int song = BeatmapId is int id && offsets != null ? offsets.GetSongOffset(id) : 0;

        double sound = SoundDelay is double givenSound ? Measured(givenSound) : MeasuredSound(audioManager);
        double picture = PictureDelay is double givenPicture ? Measured(givenPicture) : MeasuredPicture(host);

        string perDevice = offsets?.PerDevice != false ? "this" : "every";
        string audioDevice = offsets != null ? OffsetManager.AudioDeviceLabel(offsets.AudioDevice) : "Your audio";
        string display = offsets != null ? OffsetManager.DisplayLabel(offsets.Display) : "Your screen";

        // As the guide's numbers have them: how much later than the music's sent out taps are judged, and how far ahead
        // of the judging the notes are drawn. A song offset moves the judging and the notes together.
        int judgedLater = (int)Math.Round(orZero(sound) - audio - song);
        int drawnAhead = (int)Math.Round(orZero(picture) + visual);

        return new List<(string, string, string)>
        {
            ("Measured by Sakura", $"sound {measured(sound)} · picture {measured(picture)}", "automatic"),
            ($"+ {audioDevice}", $"audio offset {OffsetManager.Signed(audio)}", $"{perDevice} audio device"),
            ($"+ {display}", $"visual offset {OffsetManager.Signed(visual)}", $"{perDevice} display"),
            BeatmapId != null
                ? ($"+ {DifficultyName ?? "This song"}", $"song offset {OffsetManager.Signed(song)}", "this difficulty only")
                : ("+ The song", "its song offset, if it has one", "one difficulty"),
            ("= You play with",
                $"{(judgedLater >= 0 ? $"taps judged {judgedLater} ms later" : $"taps judged {-judgedLater} ms earlier")} · "
                + $"{(drawnAhead >= 0 ? $"notes drawn {drawnAhead} ms ahead" : $"notes drawn {-drawnAhead} ms behind")}",
                string.Empty)
        };
    }

    private void show(IReadOnlyList<(string What, string Value, string For)> rows)
    {
        Clear();

        bool phone = gameLayout?.IsPhone == true;

        if (Compact)
        {
            // Just the offsets, with what each is for: the measured part and the totals are the guide's to explain.
            var line = new TextFlowContainer(t =>
            {
                t.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, phone ? 17 : 14, FontWeight.Regular);
                t.Color = palette.Text;
                t.Alpha = 0.85f;
            })
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y
            };

            var parts = new List<string>();

            for (int i = 1; i < rows.Count - 1; i++)
            {
                if (i == 3 && BeatmapId == null)
                    continue;

                parts.Add($"{rows[i].Value} ({rows[i].What[2..]})");
            }

            line.Text = "You play with " + string.Join(" · ", parts);
            Add(line);
            return;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            bool total = i == rows.Count - 1;

            var row = new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Horizontal,
                Margin = new MarginPadding { Top = total ? 6 : 0 }
            };

            string[] cells = { rows[i].What, rows[i].Value, rows[i].For };

            for (int c = 0; c < cells.Length; c++)
            {
                row.Add(new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Width = columns[c],
                    AutoSizeAxes = Axes.Y,
                    Child = new SpriteText
                    {
                        Text = cells[c],
                        Font = YuukiFontUsage.GetFontUsage(c == 0 || total ? Typeface.Quicksand : Typeface.Nunito, phone ? 18 : 15,
                            c == 0 || total ? FontWeight.Bold : FontWeight.Regular),
                        Color = total && c == 1 ? palette.Control : palette.Text,
                        Alpha = c == 2 ? 0.7f : 1
                    }
                });
            }

            Add(row);
        }
    }

    /// <summary>
    /// The sound's delay Sakura measures, from its being mixed to its reaching the headphones or speakers (ms); NaN
    /// where the audio output can't say.
    /// </summary>
    public static double MeasuredSound(IAudioManager? audioManager) => Measured(audioManager?.OutputLatencyMs ?? double.NaN);

    /// <summary>
    /// The picture's delay Sakura measures, from the update a frame shows to its being on the screen (ms); NaN where
    /// the renderer can't tell.
    /// </summary>
    public static double MeasuredPicture(AppHost? host) => Measured(host?.Renderer?.DisplayLatencyMilliseconds ?? double.NaN);

    /// <summary>
    /// A delay as measured: NaN for 0, which is what a backend that doesn't measure it leaves.
    /// </summary>
    public static double Measured(double milliseconds) => milliseconds > 0 ? milliseconds : double.NaN;

    private static string measured(double milliseconds) => double.IsNaN(milliseconds) ? "not measured" : $"{milliseconds:0} ms";

    private static double orZero(double milliseconds) => double.IsNaN(milliseconds) ? 0 : milliseconds;
}
