// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Audio;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Edit.Components;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Edit.Timeline;

public partial class EditorTimeline : Container, IBlockGlobalAltScrollVolume
{
    public const float HEIGHT = 80;

    [Resolved]
    private EditorSession session { get; set; } = null!;

    private Waveform? waveformData;

    private Container content = null!;
    private WaveformGraph waveform = null!;
    private LineSet ticks = null!;
    private LineSet notes = null!;
    private Container timingLabels = null!;
    private EditorColors colors;

    private bool wasRunningBeforeDrag;

    /// <param name="waveform">The song's waveform, or null with no track. Disposed with the timeline.</param>
    public EditorTimeline(Waveform? waveform)
    {
        waveformData = waveform;
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        colors = EditorColors.From(colorProvider);

        Masking = true;
        CornerRadius = 12;

        Children = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One, Color = colors.Backdrop },
            content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                Masking = true,
                Children = new Drawable[]
                {
                    waveform = new WaveformGraph
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Height = HEIGHT,
                        Resolution = 0.5f,
                        Waveform = waveformData,
                        Color = colors.Accent.WithAlpha(0.6f),
                        LowColor = ColorExtensions.FromHex("ddffff"),
                        MidColor = colors.Accent,
                        HighColor = colors.AccentStrong
                    },
                    ticks = new LineSet { RelativeSizeAxes = Axes.Both, Size = Vector2.One },
                    notes = new LineSet { RelativeSizeAxes = Axes.Both, Size = Vector2.One },
                    timingLabels = new Container { RelativeSizeAxes = Axes.Both, Size = Vector2.One }
                }
            },
            // The playhead: fixed in the middle while the song moves under it.
            new Container
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                RelativeSizeAxes = Axes.Y,
                Size = new Vector2(10, 1),
                Children = new Drawable[]
                {
                    new Box
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        RelativeSizeAxes = Axes.Y,
                        Size = new Vector2(2, 1),
                        Color = colors.Accent
                    },
                    new Triangle
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Size = new Vector2(10, 7),
                        Rotation = 180,
                        Color = colors.Accent
                    },
                    new Triangle
                    {
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        Size = new Vector2(10, 7),
                        Color = colors.Accent
                    }
                }
            }
        };

        session.WaveformOpacity.BindValueChanged(e => waveform.Alpha = e.NewValue, true);
    }

    private float pixelsPerMs => DrawWidth / (float)session.TimelineSpan.Value;

    private double now => session.Clock.CurrentTime;

    private float xAt(double time) => DrawWidth / 2 + (float)((time - now) * pixelsPerMs);

    private double timeAt(float x) => now + (x - DrawWidth / 2) / pixelsPerMs;

    public override void Update()
    {
        base.Update();

        // The waveform is the whole song at this zoom, slid so the playhead's time sits under the marker.
        waveform.Width = (float)(session.Clock.TrackLength * pixelsPerMs);
        waveform.X = xAt(0);

        drawTicks();
        drawNotes();
    }

    // Timing point labels, reused from frame to frame rather than re-made (and re-shaped) each time.
    private readonly System.Collections.Generic.List<SpriteText> labels = new System.Collections.Generic.List<SpriteText>();
    private int labelsUsed;

    private void showLabel(float x, string text)
    {
        if (labelsUsed == labels.Count)
        {
            var label = new SpriteText
            {
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 12, FontWeight.Bold),
                Color = EditorColors.TIMING_POINT
            };

            labels.Add(label);
            timingLabels.Add(label);
        }

        var used = labels[labelsUsed++];
        used.Alpha = 1;
        used.Position = new Vector2(x + 5, 4);

        if (used.Text != text)
            used.Text = text;
    }

    private void drawTicks()
    {
        ticks.Clear();
        labelsUsed = 0;

        drawTimingTicks();

        for (int i = labelsUsed; i < labels.Count; i++)
            labels[i].Alpha = 0;
    }

    private void drawTimingTicks()
    {
        var timing = session.Beatmap.ControlPoints;

        if (!timing.HasTiming)
            return;

        int divisor = session.Divisor.Value.Value;
        double from = timeAt(0), to = timeAt(DrawWidth);
        var points = timing.TimingPoints;
        int drawn = 0;

        for (int i = 0; i < points.Count && drawn < 2000; i++)
        {
            var point = points[i];
            double sectionStart = i == 0 ? Math.Min(point.Time, from) : point.Time;
            double sectionEnd = i + 1 < points.Count ? points[i + 1].Time : double.MaxValue;

            if (sectionEnd < from || sectionStart > to)
                continue;

            double step = point.BeatLength / divisor;
            long first = (long)Math.Ceiling((Math.Max(sectionStart, from) - point.Time) / step - 1e-6);

            for (long tick = first; drawn < 2000; tick++)
            {
                double time = point.Time + tick * step;

                if (time > to || time >= sectionEnd)
                    break;

                int kind = EditorBeatDivisor.DivisorForTick(tick, divisor);
                int barLength = point.BeatsPerBar * divisor;
                bool bar = ((tick % barLength) + barLength) % barLength == 0;

                var size = EditorBeatDivisor.SizeFor(kind);
                float width = bar ? 3 : 2 * size.X;
                float height = bar ? DrawHeight : DrawHeight * size.Y * 0.5f;

                ticks.Add(new RectangleF(xAt(time) - width / 2, (DrawHeight - height) / 2, width, height), bar ? Color.White : EditorBeatDivisor.ColorFor(kind), bar ? 0.7f : 0.55f);
                drawn++;
            }

            // Where the section starts, in red with its tempo.
            if (point.Time >= from && point.Time <= to)
            {
                ticks.Add(new RectangleF(xAt(point.Time) - 2, 0, 4, DrawHeight), EditorColors.TIMING_POINT, 0.9f);
                showLabel(xAt(point.Time), $"{point.Bpm:0.##} BPM");
            }
        }
    }

    private void drawNotes()
    {
        notes.Clear();

        double from = timeAt(-20), to = timeAt(DrawWidth + 20);
        var all = session.Beatmap.Notes;

        const float row_height = 12;
        float centre = DrawHeight / 2;

        foreach (var note in all)
        {
            if (note.StartTime > to)
                break;

            if (note.EndTime < from)
                continue;

            float x = xAt(note.StartTime);
            float width = Math.Max(6, (float)(note.Duration * pixelsPerMs) + 6);
            bool selected = session.Beatmap.IsSelected(note);
            var color = selected ? colors.Selection : note.Type == NoteType.Swipe ? colors.SwipeNote : colors.LaneColor((int)note.Lane);

            notes.Add(new RectangleF(x - 3, centre - row_height / 2, width, row_height), color, selected ? 1 : 0.85f);
        }
    }

    #region Input

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        if (e.Button != MouseButton.Left)
            return false;

        wasRunningBeforeDrag = session.Clock.IsRunning;
        session.Clock.Stop();
        session.Clock.Seek(timeAt(ToLocalSpace(e.ScreenSpaceMousePosition).X));

        base.OnMouseDown(e);
        return true;
    }

    public override bool OnDragStart(MouseButtonEvent e) => true;

    // Dragging scrubs: the time under the marker follows the drag the other way, like dragging the tape.
    public override bool OnDrag(MouseEvent e)
    {
        session.Clock.Seek(session.Clock.CurrentTime - e.Delta.X / pixelsPerMs);
        return true;
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        base.OnMouseUp(e);

        if (wasRunningBeforeDrag)
            session.Clock.Start();

        wasRunningBeforeDrag = false;
        return true;
    }

    private float scrollAccumulated;

    public override bool OnScroll(ScrollEvent e)
    {
        var state = GetContainingInputManager()?.CurrentState;
        bool alt = state?.IsPressed(Key.AltLeft) == true || state?.IsPressed(Key.AltRight) == true;
        float delta = e.ScrollDelta.Y + e.ScrollDelta.X;

        if (alt)
        {
            Zoom(delta > 0 ? 1 : -1);
            return true;
        }

        scrollAccumulated += delta;

        while (Math.Abs(scrollAccumulated) >= 1)
        {
            if (scrollAccumulated > 0)
            {
                session.Clock.SeekBackward(!session.Clock.IsRunning);
                scrollAccumulated -= 1;
            }
            else
            {
                session.Clock.SeekForward(!session.Clock.IsRunning);
                scrollAccumulated += 1;
            }
        }

        return true;
    }

    /// <summary>
    /// Shows <paramref name="waveform"/> instead (a new song), disposing the old one.
    /// </summary>
    public void SetWaveform(Waveform? waveform)
    {
        waveformData?.Dispose();
        waveformData = waveform;
        this.waveform.Waveform = waveform;
    }

    /// <summary>
    /// Zooms in (positive) or out by steps, between half a second and ten seconds across.
    /// </summary>
    public void Zoom(int steps) => session.TimelineSpan.Value = Math.Clamp(session.TimelineSpan.Value * Math.Pow(0.85, steps), EditorSession.MIN_TIMELINE_SPAN, EditorSession.MAX_TIMELINE_SPAN);

    #endregion

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        waveformData?.Dispose();
    }
}
