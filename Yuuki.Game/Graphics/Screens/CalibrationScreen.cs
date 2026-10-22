// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Screens;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Beatmaps.ControlPoints;
using Yuuki.Game.Configurations;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Graphics.Screens;

/// <summary>
/// Works out the player's <see cref="YuukiSetting.AudioOffset"/> and <see cref="YuukiSetting.VisualOffset"/> by
/// having them tap along on a <see cref="MockPlayfield"/>: first to clicks they can only hear, then to notes they can
/// only see, then a check of both, judged as in play, before they're saved.
/// </summary>
/// <remarks>
/// Tapping to the clicks measures how late taps land on what's heard, which is what judging has to allow for, so it
/// becomes the audio offset. Tapping to the notes measures how late they land on what's seen; with the audio offset on
/// the clock, that's how far along the notes have to be drawn to be seen with the music, the visual offset. The same
/// touch delay is in both, so it's in the first and cancels out of what the notes need. See <see cref="TapCalibration"/>.
/// </remarks>
public partial class CalibrationScreen : YuukiScreen, IHaveScreenAction
{
    private const double beat_interval = TapCalibrationPad.DEFAULT_BEAT_INTERVAL;
    private const int loop_beats = 32;
    private const int lane_count = 4;
    private const int taps_needed = 16;

    /// <summary>
    /// The slowest a note can scroll in (the slowest scroll speed, following a song's BPM): the notes run on this far
    /// past the loop, so it has no seam at any speed.
    /// </summary>
    private const double longest_preempt = 3000;

    /// <summary>
    /// A moment on each step's result before the next, to see it.
    /// </summary>
    private const double step_pause_ms = 1500;

    /// <summary>
    /// Taps scattered wider than this give a figure worth trying for again.
    /// </summary>
    private const double uneven_spread_ms = 40;

    private enum Step
    {
        Listen,
        Watch,
        Check
    }

    public IReadOnlyList<ScreenAction> ScreenActions { get; }

    public override YuukiScreenPart ScreenPart => YuukiScreenPart.Play;

    public override bool AllowTrackChange => false;

    public override bool DimBackground => true;

    // The field keeps the size it's played at.
    protected override bool ScalesWithUI => false;

    /// <summary>
    /// Whether leaving opens the settings again, for a screen opened from there.
    /// </summary>
    public bool ReturnsToSettings { get; init; }

    [Resolved]
    private IAudioManager audioManager { get; set; } = null!;

    [Resolved(canBeNull: true)]
    private YuukiAudioManager? music { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private SettingsOverlay? settings { get; set; }

    private YuukiColorPalette palette;

    private ITrack? clickTrack;
    private IAudioChannel? clicks;
    private TrackClock? clickClock;

    private Container fieldArea = null!;
    private SpriteText stepText = null!;
    private SpriteText instructionText = null!;
    private SpriteText progressText = null!;
    private PillButton startOverButton = null!;
    private LeftBottomShearButton backButton = null!;
    private RightBottomShearButton saveButton = null!;

    private Step step;
    private TapCalibration? measuring;
    private double? heard;
    private double? seen;
    private bool resumeMusic;

    /// <summary>
    /// The field being tapped on.
    /// </summary>
    public MockPlayfield? Field { get; private set; }

    /// <summary>
    /// What the taps by ear and by eye make, once both are in.
    /// </summary>
    public (int Audio, int Visual)? Offsets => heard is double h && seen is double s
        ? ((int)Math.Clamp(Math.Round(-h), YuukiConfigManager.MIN_AUDIO_OFFSET, YuukiConfigManager.MAX_AUDIO_OFFSET),
            (int)Math.Clamp(Math.Round(s), YuukiConfigManager.MIN_VISUAL_OFFSET, YuukiConfigManager.MAX_VISUAL_OFFSET))
        : null;

    public CalibrationScreen()
    {
        ScreenActions = new List<ScreenAction>
        {
            new ScreenAction
            {
                Name = "Back",
                Action = Exit,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.Escape))
            }
        };
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        ScreenName.Value = "Calibrate";
        ScreenHelperText.Value = "Tap along to work out your offsets.";

        palette = Palette;

        // A click on every beat, and a beat and a half more than the loop: it's seeking back from half a beat past it.
        clickTrack = audioManager.CreateTrack(ClickTrack.Create(beat_interval, loop_beats + 2));
        clicks = clickTrack.GetChannel();
        clickClock = new TrackClock(clicks);

        AddRange(new Drawable[]
        {
            fieldArea = new Container
            {
                RelativeSizeAxes = Axes.Both
            },
            new Container
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Both,
                Y = 40,
                Masking = true,
                CornerRadius = 14,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = palette.Surface,
                        Alpha = 0.92f
                    },
                    new FlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FlowDirection.Vertical,
                        Spacing = new Vector2(0, 8),
                        Padding = new MarginPadding { Left = 32, Right = 32, Top = 20, Bottom = 20 },
                        Children = new Drawable[]
                        {
                            stepText = new SpriteText
                            {
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 18, FontWeight.Bold),
                                Color = palette.Accent
                            },
                            instructionText = new SpriteText
                            {
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 26, FontWeight.Bold),
                                Color = palette.TextLight
                            },
                            progressText = new SpriteText
                            {
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 20, FontWeight.Medium),
                                Color = palette.TextMuted
                            },
                            startOverButton = new PillButton(palette.Control, palette.TextLight)
                            {
                                Text = "Start over",
                                Action = () => begin(Step.Listen)
                            }
                        }
                    }
                }
            },
            backButton = new LeftBottomShearButton
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Position = LeftBottomShearButton.DEFAULT_POSITION,
                Action = Exit,
                BackgroundColor = palette.Surface
            },
            saveButton = new RightBottomShearButton
            {
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = RightBottomShearButton.DEFAULT_POSITION,
                Text = "Save",
                Icon = IconUsage.Check,
                Action = Save,
                BackgroundColor = palette.Surface,
                Alpha = 0
            }
        });
    }

    public override void OnEntering(Screen? last)
    {
        base.OnEntering(last);

        // The clicks are all there is to hear.
        if (music?.IsPlaying == true)
        {
            music.TogglePlayback();
            resumeMusic = true;
        }

        begin(Step.Listen);
    }

    public override void OnExiting(Screen? next)
    {
        base.OnExiting(next);

        clicks?.Stop();

        if (resumeMusic && music?.IsPlaying == false)
            music.TogglePlayback();

        if (ReturnsToSettings)
            settings?.Show();
    }

    private void begin(Step next)
    {
        step = next;

        if (next == Step.Listen)
        {
            heard = null;
            seen = null;
        }

        // Nothing on screen keeps time while listening, not even the line's pulse: a chart without notes or a tempo.
        var chart = next == Step.Listen ? new PlayableBeatmap(Array.Empty<BeatmapNote>(), lane_count) : beatChart();

        fieldArea.Clear();
        fieldArea.Add(Field = new MockPlayfield(chart, beat_interval / 2 + loop_beats * beat_interval, loop_beats * beat_interval, clickClock)
        {
            AllowJudgement = next == Step.Check,
            AudioOffset = next == Step.Check ? Offsets?.Audio ?? 0 : 0,
            VisualOffset = next == Step.Check ? Offsets?.Visual ?? 0 : 0
        });

        Field.InputReceived += onInput;

        if (clicks != null)
            clicks.Volume.Value = next == Step.Watch ? 0 : 1;

        measuring = next == Step.Check ? null : new TapCalibration(beat_interval);

        saveButton.FadeTo(next == Step.Check ? 1 : 0, 200, Easing.OutQuint);
        startOverButton.Alpha = next == Step.Listen ? 0 : 1;

        updateText();
    }

    private void onInput(ReplayInput input)
    {
        if (input.Kind != ReplayInputKind.LanePress || measuring == null)
            return;

        measuring.Add(input.Time);

        if (measuring.Counted >= taps_needed)
        {
            if (step == Step.Listen)
                heard = measuring.Offset;
            else
                seen = measuring.Offset;

            var finished = measuring;
            measuring = null;
            updateText(finished);

            Scheduler.AddDelayed(() => begin(step == Step.Listen ? Step.Watch : Step.Check), step_pause_ms);
            return;
        }

        updateText();
    }

    private void updateText(TapCalibration? finished = null)
    {
        switch (step)
        {
            case Step.Listen:
                stepText.Text = "1 of 3 · By ear";
                instructionText.Text = "Tap any lane on each click";
                break;

            case Step.Watch:
                stepText.Text = "2 of 3 · By eye";
                instructionText.Text = "Tap any lane as each note reaches the line";
                break;

            case Step.Check:
                stepText.Text = "3 of 3 · Check";
                instructionText.Text = "Play along: the notes should meet the line on each click";
                break;
        }

        if (finished != null)
        {
            progressText.Text = finished.Offset is double offset ? $"{describe(offset)}, give or take {finished.Spread ?? 0:0} ms"
                                                                    + (finished.Spread > uneven_spread_ms ? " (uneven: start over for a closer figure)" : string.Empty)
                : string.Empty;
            return;
        }

        if (step == Step.Check && Offsets is var (audio, visual))
            progressText.Text = $"Audio offset {signed(audio)} · Visual offset {signed(visual)}";
        else if (measuring != null)
            progressText.Text = $"{Math.Min(measuring.Counted, taps_needed)} / {taps_needed}";
    }

    /// <summary>
    /// Keeps both offsets, once they're worked out, and leaves.
    /// </summary>
    public void Save()
    {
        if (config != null && Offsets is var (audio, visual))
        {
            config.Get<int>(YuukiSetting.AudioOffset).Value = audio;
            config.Get<int>(YuukiSetting.VisualOffset).Value = visual;
        }

        Exit();
    }

    /// <summary>
    /// A tap note on every beat, across the lanes in turn, from the first beat to well past the loop.
    /// </summary>
    private static PlayableBeatmap beatChart()
    {
        var notes = new List<BeatmapNote>();
        double end = beat_interval / 2 + loop_beats * beat_interval + longest_preempt + beat_interval;

        for (int beat = 1; beat * beat_interval <= end; beat++)
        {
            notes.Add(new BeatmapNote
            {
                Lane = (NoteLane)(beat % lane_count),
                Type = NoteType.Tap,
                StartTime = beat * beat_interval,
                EndTime = beat * beat_interval
            });
        }

        return new PlayableBeatmap(notes.ToArray(), lane_count) { ControlPoints = ControlPointInfo.FromBpm(60000 / beat_interval) };
    }

    private static string describe(double offset) => Math.Abs(offset) < 0.5
        ? "On the beat"
        : $"{Math.Abs(offset):0} ms {(offset > 0 ? "late" : "early")}";

    private static string signed(int milliseconds) => milliseconds > 0 ? $"+{milliseconds} ms" : $"{milliseconds} ms";

    protected override void Dispose(bool isDisposing)
    {
        clicks?.Dispose();
        (clickTrack as IDisposable)?.Dispose();

        base.Dispose(isDisposing);
    }
}
