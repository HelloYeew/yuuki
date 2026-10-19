// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Edit.Components;

public partial class EditorBottomBar : Container
{
    public const float HEIGHT = 60;

    private const float spacing = 10;
    private const float time_width = 180;
    private const float playback_width = 290;
    private const float test_width = 120;

    private readonly Action onTest;

    private TimeInfoDisplay timeInfo = null!;
    private SummaryTimeline summary = null!;
    private PlaybackControl playback = null!;
    private EditorButton test = null!;

    /// <param name="onTest">Runs on the Test button.</param>
    public EditorBottomBar(Action onTest)
    {
        this.onTest = onTest;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Height = HEIGHT;

        AddRange(new Drawable[]
        {
            timeInfo = new TimeInfoDisplay { Size = new Vector2(time_width, HEIGHT) },
            summary = new SummaryTimeline { Height = HEIGHT },
            playback = new PlaybackControl { Size = new Vector2(playback_width, HEIGHT) },
            test = new EditorButton(IconUsage.SportsEsports, "Test", 18)
            {
                Size = new Vector2(test_width, HEIGHT),
                CornerRadius = 12,
                Active = true,
                Action = onTest
            }
        });
    }

    public override void Update()
    {
        base.Update();

        summary.X = time_width + spacing;
        summary.Width = Math.Max(0, DrawWidth - time_width - playback_width - test_width - spacing * 3);
        playback.X = summary.X + summary.Width + spacing;
        test.X = playback.X + playback_width + spacing;
    }
}

public partial class TimeInfoDisplay : EditorBarPanel
{
    [Resolved]
    private EditorSession session { get; set; } = null!;

    private SpriteText time = null!;
    private SpriteText details = null!;
    private string? shownDetails;

    [BackgroundDependencyLoader]
    private void load()
    {
        AddRange(new Drawable[]
        {
            time = new SpriteText
            {
                Position = new Vector2(16, 7),
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 24, FontWeight.Bold),
                Color = Color.White
            },
            details = new SpriteText
            {
                Position = new Vector2(17, 36),
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.Bold),
                Color = Color.White.WithAlpha(0.6f)
            }
        });
    }

    public override void Update()
    {
        base.Update();

        double now = session.Clock.CurrentTime;
        string text = FormatTime(now);

        if (time.Text != text)
            time.Text = text;

        var timing = session.Beatmap.ControlPoints;
        double length = session.Clock.TrackLength;
        int percent = length > 0 ? (int)(now / length * 100) : 0;

        string detailText = timing.HasTiming
            ? $"{timing.TimingPointAt(now).Bpm:0.##} BPM  ·  {percent}%"
            : $"No timing  ·  {percent}%";

        if (detailText != shownDetails)
            details.Text = shownDetails = detailText;
    }

    public static string FormatTime(double ms)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}:{span.Milliseconds:000}";
    }
}

/// <summary>
/// The whole song in one strip: how dense the notes are along it, the timing points (red), the preview
/// point (green), and the playhead. Click or drag anywhere to go there.
/// </summary>
public partial class SummaryTimeline : EditorBarPanel
{
    private const float padding = 16;
    private const float bucket_width = 3;

    [Resolved]
    private EditorSession session { get; set; } = null!;

    private LineSet lines = null!;
    private Box playhead = null!;
    private EditorColors colors;

    // Notes per bucket along the strip, rebuilt when the notes change or the strip is resized.
    private int[] density = Array.Empty<int>();
    private bool densityValid;
    private float densityWidth;

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        colors = EditorColors.From(colorProvider);

        AddRange(new Drawable[]
        {
            lines = new LineSet { RelativeSizeAxes = Axes.Both, Size = Vector2.One },
            playhead = new Box
            {
                Y = 8,
                Size = new Vector2(3, 0),
                Color = colors.Accent
            }
        });

        session.Beatmap.NotesChanged += invalidateDensity;
    }

    private void invalidateDensity() => densityValid = false;

    private float trackWidth => Math.Max(1, DrawWidth - padding * 2);

    private float xAt(double time) => padding + (float)(time / Math.Max(1, session.Clock.TrackLength)) * trackWidth;

    public override void Update()
    {
        base.Update();

        if (!densityValid || densityWidth != trackWidth)
            rebuildDensity();

        lines.Clear();

        float centre = DrawHeight / 2;
        float now = xAt(session.Clock.CurrentTime);

        // The track, the part already played lit.
        lines.Add(new RectangleF(padding, centre - 1.5f, trackWidth, 3), Color.White, 0.15f);
        lines.Add(new RectangleF(padding, centre - 1.5f, now - padding, 3), colors.Accent, 0.6f);

        // How busy each stretch is, as bars rising from the track.
        int max = 1;

        foreach (int count in density)
            max = Math.Max(max, count);

        for (int i = 0; i < density.Length; i++)
        {
            if (density[i] == 0)
                continue;

            float height = 4 + 14 * density[i] / (float)max;
            lines.Add(new RectangleF(padding + i * bucket_width, centre - height - 2, bucket_width - 1, height), Color.White, 0.35f);
        }

        foreach (var point in session.Beatmap.ControlPoints.TimingPoints)
            lines.Add(new RectangleF(xAt(point.Time) - 1, centre + 3, 2, 14), EditorColors.TIMING_POINT, 0.9f);

        int preview = session.Beatmap.BeatmapSet.PreviewTime;

        if (preview > 0)
            lines.Add(new RectangleF(xAt(preview) - 1, centre + 3, 2, 14), EditorColors.PREVIEW_POINT, 0.9f);

        playhead.X = now - 1.5f;
        playhead.Height = DrawHeight - 16;
    }

    private void rebuildDensity()
    {
        densityValid = true;
        densityWidth = trackWidth;

        int buckets = Math.Max(1, (int)(trackWidth / bucket_width));

        if (density.Length != buckets)
            density = new int[buckets];
        else
            Array.Clear(density);

        double length = Math.Max(1, session.Clock.TrackLength);

        foreach (var note in session.Beatmap.Notes)
        {
            int bucket = (int)(note.StartTime / length * buckets);
            density[Math.Clamp(bucket, 0, buckets - 1)]++;
        }
    }

    private void seekTo(Vector2 screenPosition)
    {
        float x = ToLocalSpace(screenPosition).X;
        double fraction = Math.Clamp((x - padding) / trackWidth, 0, 1);
        session.Clock.Seek(fraction * session.Clock.TrackLength);
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        if (e.Button != MouseButton.Left)
            return false;

        seekTo(e.ScreenSpaceMousePosition);
        base.OnMouseDown(e);
        return true;
    }

    public override bool OnDragStart(MouseButtonEvent e) => e.Button == MouseButton.Left;

    public override bool OnDrag(MouseEvent e)
    {
        seekTo(e.ScreenSpaceMousePosition);
        return true;
    }

    protected override void Dispose(bool isDisposing)
    {
        session.Beatmap.NotesChanged -= invalidateDensity;
        base.Dispose(isDisposing);
    }
}

public partial class PlaybackControl : EditorBarPanel
{
    private static readonly double[] rates = { 0.25, 0.5, 0.75, 1 };

    [Resolved]
    private EditorSession session { get; set; } = null!;

    private EditorButton playPause = null!;
    private readonly EditorButton[] rateButtons = new EditorButton[rates.Length];
    private bool shownRunning;

    [BackgroundDependencyLoader]
    private void load()
    {
        Add(playPause = new EditorButton(IconUsage.PlayArrow)
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            X = 10,
            Size = new Vector2(44, 40),
            Action = () => session.Clock.Toggle()
        });

        for (int i = 0; i < rates.Length; i++)
        {
            double rate = rates[i];

            Add(rateButtons[i] = new EditorButton(text: $"{rate * 100:0}%", fontSize: 13)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = 64 + i * 54,
                Size = new Vector2(50, 32),
                Action = () => session.Clock.Rate = rate
            });
        }
    }

    public override void Update()
    {
        base.Update();

        if (session.Clock.IsRunning != shownRunning)
        {
            shownRunning = session.Clock.IsRunning;
            playPause.Icon = shownRunning ? IconUsage.Pause : IconUsage.PlayArrow;
        }

        for (int i = 0; i < rates.Length; i++)
            rateButtons[i].Active = Math.Abs(session.Clock.Rate - rates[i]) < 0.001;
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        base.OnMouseDown(e);
        return true;
    }
}
