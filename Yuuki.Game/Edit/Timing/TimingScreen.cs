// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Beatmaps.ControlPoints;
using Yuuki.Game.Edit.Components;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Edit.Timing;

/// <summary>
/// The Timing tab
/// </summary>
public partial class TimingScreen : Container
{
    private const float margin = 10;
    private const float panel_width = 380;

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    [Resolved]
    private EditorSession session { get; set; } = null!;

    private FlowContainer rows = null!;
    private Container listPanel = null!;
    private SpriteText emptyText = null!;
    private EditorPanel settings = null!;
    private MetronomePanel metronomePanel = null!;

    /// <summary>
    /// The settings and metronome down the right.
    /// </summary>
    public EditorPanelColumn SettingsColumn { get; private set; } = null!;

    /// <summary>
    /// The metronome, under the timing point's settings.
    /// </summary>
    public MetronomeDisplay Metronome => metronomePanel.Metronome;

    private EditorTextBox timeBox = null!;
    private EditorTextBox bpmBox = null!;
    private EditorButton removeButton = null!;
    private readonly Dictionary<int, EditorButton> meterButtons = new Dictionary<int, EditorButton>();
    private readonly List<Drawable> pointControls = new List<Drawable>();

    private readonly List<TimingPointRow> pointRows = new List<TimingPointRow>();

    // The selected point, by its time, since undo swaps in new point objects.
    private double? selectedTime;

    private readonly TapTempo tapTempo = new TapTempo();

    [BackgroundDependencyLoader]
    private void load()
    {
        listPanel = new EditorBarPanel
        {
            Position = new Vector2(margin, 0),
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Position = new Vector2(16, 12),
                    Text = "TIMING POINTS",
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.Bold),
                    Color = Color.White.WithAlpha(0.6f)
                },
                new YuukiScrollableContainer
                {
                    Position = new Vector2(0, 36),
                    RelativeSizeAxes = Axes.Both,
                    Size = Vector2.One,
                    Padding = new MarginPadding { Bottom = 36 },
                    Child = rows = new FlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        Width = 1,
                        AutoSizeAxes = Axes.Y,
                        Direction = FlowDirection.Vertical,
                        Spacing = new Vector2(0, 4),
                        Padding = new MarginPadding { Left = 10, Right = 10, Bottom = 10 }
                    }
                },
                emptyText = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "No timing yet. Go to where a beat lands and add the first point.",
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Bold),
                    Color = Color.White.WithAlpha(0.6f)
                }
            }
        };

        settings = new EditorPanel("Timing point");

        timeBox = new EditorTextBox { Width = 140 };
        timeBox.Committed += text => applyNumber(text, value => value >= 0, (point, value) => point.Time = Math.Round(value));

        bpmBox = new EditorTextBox { Width = 140 };
        bpmBox.Committed += text => applyNumber(text, value => value > 0, (point, value) => point.Bpm = value);

        settings.AddRow(row("Time (ms)", timeBox, button(IconUsage.AccessTime, "Now", 100, useCurrentTime)));
        settings.AddRow(row(string.Empty,
            button(null, "-10", 48, () => nudge(-10)),
            button(null, "-1", 48, () => nudge(-1)),
            button(null, "+1", 48, () => nudge(1)),
            button(null, "+10", 48, () => nudge(10))));
        settings.AddRow(row("BPM", bpmBox, button(IconUsage.MusicNote, "Tap", 100, tap)));
        settings.AddRow(row(string.Empty,
            button(null, "×½", 60, () => scaleTempo(0.5)),
            button(null, "×2", 60, () => scaleTempo(2))));

        var meters = new List<Drawable>();

        foreach (int beats in new[] { 3, 4, 5, 6, 7 })
        {
            var meter = button(null, $"{beats}/4", 42, () => setMeter(beats));
            meterButtons[beats] = meter;
            meters.Add(meter);
        }

        settings.AddRow(row("Meter", meters.ToArray()));
        settings.AddRow(new Container { Height = 4 });

        // 130 wide, so both fit after the labels' 82 in the panel's 360 (at 150, Remove was cut off).
        removeButton = button(IconUsage.Delete, "Remove", 130, removeSelected);

        settings.AddRow(row(string.Empty,
            button(IconUsage.Add, "Add here", 130, AddAtCurrentTime),
            removeButton));

        metronomePanel = new MetronomePanel();
        metronomePanel.SetCompact(gameLayout?.IsPhone == true);

        // The settings and the metronome down the right, scrolling when they outgrow the tab (a big UI
        // scale). The list on the left scrolls on its own.
        var column = new EditorPanelColumn(panel_width)
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Position = new Vector2(-(margin - EditorPanelColumn.SHADOW_ROOM), -EditorPanelColumn.SHADOW_ROOM),

            // Flush with the tab's top and bottom, like the list; the shadow room reaches past them.
            EdgeMargin = -EditorPanelColumn.SHADOW_ROOM
        };

        SettingsColumn = column;

        column.AddPanel(settings);
        column.AddPanel(metronomePanel);

        AddRange(new Drawable[] { listPanel, column });

        session.Beatmap.TimingChanged += onTimingChanged;
        onTimingChanged();
    }

    private EditorButton button(IconUsage? icon, string text, float width, Action action)
    {
        var created = new EditorButton(icon, text, 14) { Size = new Vector2(width, 34), Action = action };

        if (icon != IconUsage.Add)
            pointControls.Add(created);

        return created;
    }

    /// <summary>
    /// A label, then <paramref name="items"/> side by side.
    /// </summary>
    private static Container row(string label, params Drawable[] items)
    {
        var container = new Container { Height = 34 };

        container.Add(new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Text = label,
            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 14, FontWeight.Bold),
            Color = Color.White.WithAlpha(0.7f)
        });

        float x = 82;

        foreach (var item in items)
        {
            item.Anchor = Anchor.CentreLeft;
            item.Origin = Anchor.CentreLeft;
            item.X = x;
            container.Add(item);
            x += item.Width + 6;
        }

        return container;
    }

    #region State

    private TimingPoint? selected
    {
        get
        {
            if (selectedTime is not double time)
                return null;

            var points = session.Beatmap.ControlPoints.TimingPoints;
            return points.FirstOrDefault(p => Math.Abs(p.Time - time) < 0.5);
        }
    }

    private void select(TimingPoint? point, bool seek)
    {
        selectedTime = point?.Time;

        if (point != null && seek)
            session.Clock.SeekSmoothlyTo(point.Time);

        updateSelection();
    }

    private void onTimingChanged()
    {
        var points = session.Beatmap.ControlPoints.TimingPoints;

        // Gone (removed, or undone): the point in force at the playhead takes its place.
        if (selected == null)
            selectedTime = points.Count > 0 ? session.Beatmap.ControlPoints.TimingPointAt(session.Clock.CurrentTime).Time : null;

        while (pointRows.Count < points.Count)
        {
            var created = new TimingPointRow(this);
            pointRows.Add(created);
            rows.Add(created);
        }

        while (pointRows.Count > points.Count)
        {
            var last = pointRows[^1];
            pointRows.RemoveAt(pointRows.Count - 1);
            rows.Remove(last);
        }

        for (int i = 0; i < points.Count; i++)
            pointRows[i].Display(points[i], i == 0);

        emptyText.Alpha = points.Count == 0 ? 1 : 0;
        updateSelection();
    }

    private void updateSelection()
    {
        var point = selected;

        foreach (var pointRow in pointRows)
            pointRow.Selected = point != null && ReferenceEquals(pointRow.Point, point);

        foreach (var control in pointControls)
        {
            if (control is EditorButton b)
                b.Enabled = point != null;
        }

        timeBox.Alpha = bpmBox.Alpha = point != null ? 1 : 0.4f;

        foreach (var (beats, meterButton) in meterButtons)
            meterButton.Active = point?.BeatsPerBar == beats;
    }

    public override void Update()
    {
        base.Update();

        listPanel.Size = new Vector2(Math.Max(0, DrawWidth - margin * 3 - panel_width), DrawHeight);

        // Only on this tab, and quiet while a tempo is being tapped, so the taps are heard alone.
        Metronome.Active = Alpha > 0.01f;
        Metronome.ClickingEnabled = Clock.CurrentTime - tapTempo.LastTapTime > 2000;

        var point = selected;

        // The fields show the point as it is whenever they aren't being typed in, which also puts back
        // anything typed that didn't make sense.
        timeBox.SetText(point != null ? point.Time.ToString("0", CultureInfo.InvariantCulture) : string.Empty);
        bpmBox.SetText(point != null ? point.Bpm.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty);
    }

    #endregion

    #region Edits

    private void edit(Action<TimingPoint> change)
    {
        var point = selected;

        if (point == null)
            return;

        session.Beatmap.UpdateTiming(_ => change(point));
        selectedTime = point.Time;
        updateSelection();
    }

    // Typed numbers that don't parse or make no sense are dropped; Update puts the field back.
    private void applyNumber(string text, Func<double, bool> valid, Action<TimingPoint, double> apply)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || !valid(value))
            return;

        edit(point => apply(point, value));
    }

    private void useCurrentTime() => edit(point => point.Time = Math.Round(session.Clock.CurrentTime));

    private void nudge(double ms) => edit(point => point.Time = Math.Max(0, point.Time + ms));

    private void scaleTempo(double factor) => edit(point => point.BeatLength /= factor);

    private void setMeter(int beats) => edit(point => point.TimeSignature = new TimeSignature(beats));

    private void tap()
    {
        double? bpm = tapTempo.Tap(Clock.CurrentTime);

        if (bpm is double value)
            edit(point => point.Bpm = Math.Round(value, 2));
    }

    /// <summary>
    /// Adds a timing point at the playhead with the tempo and meter in force there (120 BPM for the first).
    /// A point already there is selected instead.
    /// </summary>
    public void AddAtCurrentTime()
    {
        double time = Math.Round(session.Clock.CurrentTime);
        var timing = session.Beatmap.ControlPoints;

        var existing = timing.TimingPoints.FirstOrDefault(p => Math.Abs(p.Time - time) < 1);

        if (existing != null)
        {
            select(existing, false);
            return;
        }

        TimingPoint point;

        if (timing.HasTiming)
        {
            var current = timing.TimingPointAt(time);
            point = new TimingPoint(time, current.BeatLength, current.TimeSignature);
        }
        else
        {
            float bpm = session.Beatmap.BeatmapSet.BPM;
            point = new TimingPoint(time, 60000 / (bpm > 0 ? bpm : 120));
        }

        session.Beatmap.UpdateTiming(info => info.Add(point));
        select(point, false);
    }

    private void removeSelected()
    {
        var point = selected;

        if (point == null)
            return;

        session.Beatmap.UpdateTiming(info => info.Remove(point));
        selectedTime = null;
        onTimingChanged();
    }

    #endregion

    protected override void Dispose(bool isDisposing)
    {
        session.Beatmap.TimingChanged -= onTimingChanged;
        base.Dispose(isDisposing);
    }

    /// <summary>
    /// One timing point in the list: a red marker, where it is, its tempo and meter. Click to select it
    /// and go there.
    /// </summary>
    private partial class TimingPointRow : Container
    {
        private readonly TimingScreen screen;
        private readonly Box background;
        private readonly SpriteText time;
        private readonly SpriteText tempo;
        private readonly SpriteText meter;
        private EditorColors colors;
        private bool colorsReady;
        private bool selected;

        [Resolved(canBeNull: true)]
        private UISoundPlayer? uiSounds { get; set; }

        public TimingPoint? Point { get; private set; }

        public TimingPointRow(TimingScreen screen)
        {
            this.screen = screen;

            // The width of the list, inside its padding.
            RelativeSizeAxes = Axes.X;
            Width = 1;
            Height = 38;
            Masking = true;
            CornerRadius = 8;

            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One },
                new Box
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 10,
                    Size = new Vector2(4, 22),
                    Color = EditorColors.TIMING_POINT
                },
                time = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 26,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 17, FontWeight.Bold)
                },
                tempo = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 150,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Bold)
                },
                meter = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 270,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Bold),
                    Color = Color.White.WithAlpha(0.6f)
                }
            };
        }

        [BackgroundDependencyLoader]
        private void load([CanBeNull] IYuukiColorProvider? colorProvider)
        {
            colors = EditorColors.From(colorProvider);
            colorsReady = true;
            updateState(false);
        }

        public void Display(TimingPoint point, bool first)
        {
            Point = point;
            time.Text = TimeInfoDisplay.FormatTime(point.Time);
            tempo.Text = $"{point.Bpm:0.##} BPM";
            meter.Text = first ? $"{point.TimeSignature} · offset" : point.TimeSignature.ToString();
        }

        public bool Selected
        {
            set
            {
                if (selected == value)
                    return;

                selected = value;
                updateState(IsHovered);
            }
        }

        private void updateState(bool hovered)
        {
            if (!colorsReady)
                return;

            background.FadeToColor(selected ? colors.AccentStrong : hovered ? colors.ControlHover : colors.Control, 150, Easing.OutQuint);
        }

        public override bool OnHover(MouseEvent e)
        {
            updateState(true);
            uiSounds?.Play(UISound.Hover);
            return true;
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            updateState(false);
            return true;
        }

        public override bool OnMouseDown(MouseButtonEvent e)
        {
            base.OnMouseDown(e);
            return true;
        }

        public override bool OnClick(MouseButtonEvent e)
        {
            if (e.Button != MouseButton.Left || Point == null)
                return false;

            uiSounds?.Play(UISound.Select);
            screen.select(Point, true);
            return true;
        }
    }
}

/// <summary>
/// Tap tempo, the average of the last few taps' spacing, starting over after a pause.
/// </summary>
public class TapTempo
{
    private const int max_taps = 8;
    private const int min_taps = 4;
    private const double reset_after = 2000;

    private readonly List<double> taps = new List<double>();

    /// <summary>
    /// When the last tap was (ms), or -infinity before the first.
    /// </summary>
    public double LastTapTime { get; private set; } = double.NegativeInfinity;

    /// <summary>
    /// Records a tap at <paramref name="time"/> (ms). Returns the tempo once there are enough taps to tell.
    /// </summary>
    public double? Tap(double time)
    {
        if (taps.Count > 0 && time - taps[^1] > reset_after)
            taps.Clear();

        taps.Add(time);
        LastTapTime = time;

        if (taps.Count > max_taps)
            taps.RemoveAt(0);

        if (taps.Count < min_taps)
            return null;

        double interval = (taps[^1] - taps[0]) / (taps.Count - 1);
        return interval > 0 ? 60000 / interval : null;
    }
}
