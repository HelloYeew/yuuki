// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Screens;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Graphics.Screens;

public partial class ReplayScreen : YuukiScreen, IHaveScreenAction
{
    /// <summary>
    /// How far the wheel and the arrow keys skip, in ms.
    /// </summary>
    private const double skip_distance = 5000;

    /// <summary>
    /// The shortest gap between two seeks while a drag is moving; each one re-plays the run.
    /// </summary>
    private const double drag_seek_interval = 120;

    /// <summary>
    /// The replay ends this long after the last note.
    /// </summary>
    private const double end_margin = 2000;

    private static readonly double[] speeds = { 0.5, 0.75, 1, 1.5, 2 };

    public IReadOnlyList<ScreenAction> ScreenActions { get; }

    public override YuukiScreenPart ScreenPart => YuukiScreenPart.Play;

    public override bool AllowTrackChange => false;

    public override bool HideFooter => true;

    public override bool LoopPreview => false;

    public override bool HideVideo => false;

    public override bool DimBackground => true;

    protected override bool ScalesWithUI => false;

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiAudioManager? audioManager { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapNoteStore? noteStore { get; set; }

    private readonly ScoreInfo score;
    private readonly ReplayData replay;

    private PlayableBeatmap? chart;
    private Beatmap? loadedNotesFor;
    private GameplayContainer? gameplay;
    private YuukiColorPalette palette;

    private PillButton pauseButton = null!;
    private readonly List<SpeedButton> speedButtons = new List<SpeedButton>();

    private double endTime;

    // Watching straight after a play: the gameplay screen underneath still owns the song's channel
    // (its Retry needs it), so the replay plays on that one and hands it back, instead of opening its own.
    private bool borrowedChannel;

    private bool paused;
    private bool ended;
    private int speedIndex = Array.IndexOf(speeds, 1.0);
    private double lastDragSeek = double.NegativeInfinity;

    /// <summary>
    /// Whether the replay is playing (false when paused, ended, or it couldn't be shown).
    /// </summary>
    public bool IsPlaying => gameplay != null && !paused && !ended;

    /// <summary>
    /// The replay's gameplay (null when it couldn't be shown). For tests.
    /// </summary>
    public GameplayContainer? Gameplay => gameplay;

    /// <summary>
    /// The playback speed.
    /// </summary>
    public double Speed => speeds[speedIndex];

    public ReplayScreen(ScoreInfo score, ReplayData replay)
    {
        this.score = score;
        this.replay = replay;

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
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        ScreenName.Value = "Replay";
        ScreenHelperText.Value = $"Played {score.Date.LocalDateTime:d MMM yyyy, HH:mm}";

        palette = Palette;

        chart = loadChart();

        if (chart == null || !replay.CanPlayOn(chart))
        {
            // The chart was edited (or is gone) since the play: its inputs would be judged differently.
            Add(message("This replay can't be shown", "The beatmap's notes have changed since this play."));
            return;
        }

        endTime = chart.LastNoteTime + end_margin;

        borrowedChannel = audioManager?.InGameplay == true && audioManager.GameplayChannel != null;

        IAudioChannel? channel = borrowedChannel ? audioManager!.GameplayChannel : audioManager?.BeginGameplay();
        IAdjustableClock? source = channel != null ? new TrackClock(channel) : null;

        AddRange(new Drawable[]
        {
            gameplay = new GameplayContainer(chart, source)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Replay = replay,
                // Played back as it was played: the same offset lines the inputs up with the music.
                AudioOffset = replay.AudioOffset,
                Preempt = GameplayScreen.ScrollPreempt(config, chart),
                LeadInTime = GameplayScreen.LeadInFor(chart),
                SongLength = channel != null && channel.Length > 0 ? channel.Length : null
            },
            createPlaybackPanel()
        });

        gameplay.Failed += () => Scheduler.Add(finish);
    }

    /// <summary>The beatmap's own chart, as gameplay builds it; null when it has no notes (or none is selected).</summary>
    private PlayableBeatmap? loadChart()
    {
        Beatmap? beatmap = workingBeatmap?.Beatmap;
        if (beatmap == null || beatmap.ID != score.BeatmapId)
            return null;

        BeatmapNote[] notes = noteStore?.Load(beatmap) ?? beatmap.Notes;
        if (notes.Length == 0)
            return null;

        loadedNotesFor = beatmap;
        return PlayableBeatmap.FromBeatmap(beatmap, laneCount: LaneKeyLayouts.Clamp(beatmap.LaneCount));
    }

    #region Layout

    private Drawable createPlaybackPanel()
    {
        var speedRow = new FlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FlowDirection.Horizontal,
            Spacing = new Vector2(6, 0)
        };

        for (int i = 0; i < speeds.Length; i++)
        {
            int index = i;
            var button = new SpeedButton(speeds[i], palette) { Action = () => setSpeed(index) };
            speedButtons.Add(button);
            speedRow.Add(button);
        }

        updateSpeedButtons();

        return new Container
        {
            Anchor = Anchor.CentreRight,
            Origin = Anchor.CentreRight,
            X = -36,
            AutoSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 12,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = palette.Backdrop,
                    Alpha = 0.85f
                },
                new FlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 10),
                    Padding = new MarginPadding(18),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = "REPLAY",
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 24, FontWeight.Bold),
                            Color = palette.TextLight
                        },
                        new SpriteText
                        {
                            Text = $"{score.Score:N0}  ·  {score.Accuracy * 100:0.00}%  ·  {score.Rank}",
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 18, FontWeight.Regular),
                            Color = palette.TextLight
                        },
                        pauseButton = new PillButton(palette.Control, palette.TextLight)
                        {
                            Margin = new MarginPadding { Top = 4 },
                            Text = pauseLabel,
                            Action = togglePause
                        },
                        new SpriteText
                        {
                            Margin = new MarginPadding { Top = 4 },
                            Text = "Speed",
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 16, FontWeight.Bold),
                            Color = palette.TextLight
                        },
                        speedRow,
                        new SpriteText
                        {
                            Margin = new MarginPadding { Top = 4 },
                            Text = "Bar or wheel: seek  ·  ← -> 5s  ·  up down speed",
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 14, FontWeight.Regular),
                            Color = palette.TextLight.WithAlpha(0.7f)
                        }
                    }
                }
            }
        };
    }

    private Drawable message(string title, string body) => new FlowContainer
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre,
        AutoSizeAxes = Axes.Both,
        Direction = FlowDirection.Vertical,
        Spacing = new Vector2(0, 8),
        Children = new Drawable[]
        {
            new SpriteText
            {
                Text = title,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 40, FontWeight.Bold),
                Color = palette.TextLight
            },
            new SpriteText
            {
                Text = $"{body}  Press Escape to go back.",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 20, FontWeight.Regular),
                Color = palette.TextLight
            }
        }
    };

    #endregion

    #region Lifecycle

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (gameplay == null)
            return;

        gameplay.Restart();
        gameplay.ProgressBarSeek = onSeekRequested;
    }

    public override void OnEntering(Screen? last)
    {
        base.OnEntering(last);
        this.FadeInFromZero(300);
    }

    public override void OnExiting(Screen? next)
    {
        base.OnExiting(next);

        if (gameplay != null)
        {
            // A borrowed song goes back at its own speed, paused, for the gameplay screen's Retry.
            gameplay.Rate = 1;
            gameplay.Stop();

            if (!borrowedChannel)
                audioManager?.EndGameplay();
        }

        this.FadeOut(300);

        if (loadedNotesFor != null)
        {
            noteStore?.Unload(loadedNotesFor);
            loadedNotesFor = null;
        }
    }

    public override void Update()
    {
        base.Update();

        if (gameplay != null && !ended && gameplay.GameplayClock.CurrentTime >= endTime)
            finish();
    }

    /// <summary>
    /// The run is over (or its player failed): back to the score.
    /// </summary>
    private void finish()
    {
        if (ended)
            return;

        ended = true;
        gameplay?.Stop();
        Scheduler.AddDelayed(() =>
        {
            if (IsCurrentScreen)
                Exit();
        }, 1000);
    }

    #endregion

    #region Controls

    private string pauseLabel => paused ? "Play  ·  Space" : "Pause  ·  Space";

    private void togglePause()
    {
        if (gameplay == null || ended)
            return;

        paused = !paused;

        if (paused)
            gameplay.Stop();
        else
            gameplay.Start();

        pauseButton.Text = pauseLabel;
    }

    /// <summary>
    /// Moves the replay to <paramref name="time"/>, kept within the run.
    /// </summary>
    public void SeekTo(double time)
    {
        if (gameplay == null || ended)
            return;

        gameplay.SeekReplay(Math.Clamp(time, -gameplay.LeadInTime, endTime - 1));
    }

    private void onSeekRequested(double time, bool final)
    {
        // Each seek re-plays the run up to the new time, so a moving drag seeks every so often and
        // the release lands exactly.
        if (!final && Clock.CurrentTime - lastDragSeek < drag_seek_interval)
            return;

        lastDragSeek = Clock.CurrentTime;
        SeekTo(time);
    }

    private void skip(double distance)
    {
        if (gameplay != null)
            SeekTo(gameplay.GameplayClock.CurrentTime + distance);
    }

    private void setSpeed(int index)
    {
        speedIndex = Math.Clamp(index, 0, speeds.Length - 1);

        if (gameplay != null)
            gameplay.Rate = speeds[speedIndex];

        updateSpeedButtons();
    }

    private void updateSpeedButtons()
    {
        for (int i = 0; i < speedButtons.Count; i++)
            speedButtons[i].Selected = i == speedIndex;
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        switch (e.Key)
        {
            case Key.Space when !e.IsRepeat:
                togglePause();
                return true;

            case Key.Left:
                skip(-skip_distance);
                return true;

            case Key.Right:
                skip(skip_distance);
                return true;

            case Key.Up when !e.IsRepeat:
                setSpeed(speedIndex + 1);
                return true;

            case Key.Down when !e.IsRepeat:
                setSpeed(speedIndex - 1);
                return true;
        }

        return base.OnKeyDown(e);
    }

    public override bool OnScroll(ScrollEvent e)
    {
        // Wheel down moves on, wheel up goes back, like scrolling down a timeline.
        if (e.ScrollDelta.Y < 0)
            skip(skip_distance);
        else if (e.ScrollDelta.Y > 0)
            skip(-skip_distance);

        return true;
    }

    #endregion

    /// <summary>
    /// A small speed choice: filled when it's the current speed.
    /// </summary>
    private partial class SpeedButton : ClickableContainer
    {
        private readonly Box background;
        private readonly SpriteText label;
        private readonly YuukiColorPalette palette;
        private bool selected;

        public bool Selected
        {
            set
            {
                selected = value;
                background.FadeToColor(value ? palette.Control : palette.Surface.Lighten(0.2f), 150, Easing.OutQuint);
                label.FadeToColor(value ? Color.White : palette.Text, 150, Easing.OutQuint);
            }
        }

        public SpeedButton(double speed, YuukiColorPalette palette)
        {
            this.palette = palette;

            Size = new Vector2(48, 30);
            Masking = true;
            CornerRadius = 8;

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = palette.Surface.Lighten(0.2f)
                },
                label = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = $"{speed:0.##}×",
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 16, FontWeight.Bold),
                    Color = palette.Text
                },
                new HoverClickSounds(UISound.Select)
            };
        }

        public override bool OnHover(MouseEvent e)
        {
            if (!selected)
                background.FadeToColor(palette.Surface.Lighten(0.4f), 150, Easing.OutQuint);
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            if (!selected)
                background.FadeToColor(palette.Surface.Lighten(0.2f), 150, Easing.OutQuint);
            return base.OnHoverLost(e);
        }
    }
}
