// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A setup whose typical delays <see cref="DelayDiagram"/> shows.
/// </summary>
public enum DelaySetup
{
    Speakers,
    Bluetooth,
    Tv
}

/// <summary>
/// What <see cref="DelayDiagram"/> shows a beat with.
/// </summary>
public enum DelayPhase
{
    WithoutOffsets,

    /// <summary>
    /// The audio offset alone, as most games have.
    /// </summary>
    AudioOnly,

    WithOffsets
}

/// <summary>
/// One beat on its way to the player, slowed down: the sound to their ears, the picture to their eyes, and their tap
/// back to the game. Without offsets, the sound and the picture arrive apart and the tap is judged late. With the audio
/// offset alone, the tap is judged on the beat, but the notes move with the judging and are seen well after the beat is
/// heard: why there's a second. With both, the notes are drawn to be seen as the beat is heard as well.
/// </summary>
/// <remarks>
/// The delays are typical ones for a few setups, to show the idea, not the player's own: only calibrating measures
/// those. Everything on it follows <see cref="Time"/>, so it's drawn the same however it got there.
/// </remarks>
public partial class DelayDiagram : FlowContainer
{
    // The span shown, in ms from the beat: a little before it, for notes drawn early, to well after.
    private const double span_start = -100;
    private const double span_end = 300;

    /// <summary>
    /// How many times slower than real it plays.
    /// </summary>
    private const double slowdown = 8;

    // How long a phase holds once the beat's been across, to read it.
    private const double hold_ms = 2400;

    private const int phase_count = 3;

    // How long a tap waits at the game before the audio offset takes it back, and takes to get there, in the diagram's
    // ms.
    private const double offset_wait = 30;
    private const double offset_travel = 80;

    /// <summary>
    /// The setup shown.
    /// </summary>
    public DelaySetup Setup { get; private set; } = DelaySetup.Speakers;

    /// <summary>
    /// Whether the offsets are on.
    /// </summary>
    public DelayPhase Phase { get; private set; } = DelayPhase.WithoutOffsets;

    /// <summary>
    /// Whether it goes from one phase to the other by itself, which it does until one's picked.
    /// </summary>
    public bool Cycling { get; private set; } = true;

    /// <summary>
    /// How far the beat is on its way, in ms from it.
    /// </summary>
    public double Time { get; private set; } = span_start;

    /// <summary>
    /// The setup's delays, in ms: the sound reaching the ears, the picture reaching the eyes, a tap reaching the game.
    /// </summary>
    public (double Sound, double Picture, double Tap) Delays => DelaysOf(Setup);

    /// <summary>
    /// When the beat is heard.
    /// </summary>
    public double SoundHeard => Delays.Sound;

    /// <summary>
    /// When the note for the beat is drawn at the line: on the beat without offsets; with the audio offset alone, which
    /// moves the notes with the judging, when a tap on the beat that's heard reaches the game; with both, as far from
    /// the beat as the sound is behind the picture, so it's seen as the beat is heard.
    /// </summary>
    public double PictureDrawn => Phase switch
    {
        DelayPhase.AudioOnly => Delays.Sound + Delays.Tap,
        DelayPhase.WithOffsets => Delays.Sound - Delays.Picture,
        _ => 0
    };

    /// <summary>
    /// When the note is seen at the line.
    /// </summary>
    public double PictureSeen => PictureDrawn + Delays.Picture;

    /// <summary>
    /// When a tap made on the beat that's heard reaches the game.
    /// </summary>
    public double TapArrives => Delays.Sound + Delays.Tap;

    /// <summary>
    /// When that tap is judged to have been: as late as it arrives, or with the audio offset taking that back off,
    /// on the beat.
    /// </summary>
    public double TapJudged => Phase == DelayPhase.WithoutOffsets ? TapArrives : 0;

    /// <summary>
    /// Where the tap is shown at <see cref="Time"/>: on its way to the game from when the beat's heard, then, once
    /// there, eased back to when it's judged to have been.
    /// </summary>
    public double TapShownAt
    {
        get
        {
            double heard = SoundHeard;
            double arrives = TapArrives;

            if (Time < arrives)
                return heard + (arrives - heard) * Math.Clamp((Time - heard) / (arrives - heard), 0, 1);

            double back = Math.Clamp((Time - arrives - offset_wait) / offset_travel, 0, 1);
            return arrives + (TapJudged - arrives) * (1 - Math.Pow(1 - back, 3));
        }
    }

    /// <summary>
    /// Typical delays for a setup, in ms.
    /// </summary>
    public static (double Sound, double Picture, double Tap) DelaysOf(DelaySetup setup) => setup switch
    {
        // Bluetooth headphones buffer a good while of sound.
        DelaySetup.Bluetooth => (200, 50, 30),
        // A TV's picture processing, outside its game mode.
        DelaySetup.Tv => (40, 130, 30),
        _ => (40, 50, 30)
    };

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    private YuukiColorPalette palette;

    private ChoiceTab[] setupTabs = Array.Empty<ChoiceTab>();
    private ChoiceTab[] phaseTabs = Array.Empty<ChoiceTab>();
    private TextFlowContainer caption = null!;

    private double phaseStart = double.NaN;

    public DelayDiagram()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Direction = FlowDirection.Vertical;
        Spacing = new Vector2(0, 14);
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider, [CanBeNull] ScreenPalette? screenPalette)
    {
        palette = ScreenPalette.Resolve(screenPalette, colorProvider);

        setupTabs = new[]
        {
            new ChoiceTab("Speakers", palette) { Action = () => ShowSetup(DelaySetup.Speakers) },
            new ChoiceTab("Bluetooth", palette) { Action = () => ShowSetup(DelaySetup.Bluetooth) },
            new ChoiceTab("TV", palette) { Action = () => ShowSetup(DelaySetup.Tv) }
        };

        phaseTabs = new[]
        {
            new ChoiceTab("No offsets", palette) { Action = () => ShowPhase(DelayPhase.WithoutOffsets) },
            new ChoiceTab("Audio only", palette) { Action = () => ShowPhase(DelayPhase.AudioOnly) },
            new ChoiceTab("Both", palette) { Action = () => ShowPhase(DelayPhase.WithOffsets) }
        };

        Children = new Drawable[]
        {
            tabRow(setupTabs),
            tabRow(phaseTabs),
            new Timeline(this, palette),
            caption = new TextFlowContainer(t =>
            {
                t.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, gameLayout?.IsPhone == true ? 19 : 16, FontWeight.Regular);
                t.Color = palette.Text;
            })
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y
            }
        };

        updateState();
    }

    /// <summary>
    /// Shows another setup's delays, from the start of the beat.
    /// </summary>
    public void ShowSetup(DelaySetup setup)
    {
        Setup = setup;
        phaseStart = double.NaN;
        updateState();
    }

    /// <summary>
    /// Shows the beat with the offsets off or on, from its start, and stays on that.
    /// </summary>
    public void ShowPhase(DelayPhase phase)
    {
        Phase = phase;
        Cycling = false;
        phaseStart = double.NaN;
        updateState();
    }

    public override void Update()
    {
        // From the clock's time rather than its frame length, which a clock driven from outside (a test's) may not
        // report.
        double now = Clock.CurrentTime;

        if (double.IsNaN(phaseStart))
            phaseStart = now;

        double sweep = (span_end - span_start) * slowdown;

        if (now - phaseStart >= sweep + hold_ms)
        {
            phaseStart = now;

            if (Cycling)
            {
                Phase = (DelayPhase)(((int)Phase + 1) % phase_count);
                updateState();
            }
        }

        // Before the timeline updates, which draws from it. On through the hold, for what's still settling then (a late
        // tap going back to the beat).
        Time = span_start + (now - phaseStart) / slowdown;

        base.Update();
    }

    private void updateState()
    {
        for (int i = 0; i < setupTabs.Length; i++)
            setupTabs[i].Selected = (DelaySetup)i == Setup;

        for (int i = 0; i < phaseTabs.Length; i++)
            phaseTabs[i].Selected = (DelayPhase)i == Phase;

        if (caption.IsNull())
            return;

        string setup = Setup switch
        {
            DelaySetup.Bluetooth => "Bluetooth headphones play the sound well after the picture.",
            DelaySetup.Tv => "A TV outside its game mode shows the picture well after the sound.",
            _ => "Speakers and wired headphones: short delays, close together."
        };

        caption.Text = Phase switch
        {
            DelayPhase.AudioOnly =>
                $"{setup} With the audio offset alone, as most games have, that tap is judged on the beat. But it moves the notes along with the judging: they're seen {PictureSeen - SoundHeard:0} ms after the beat is heard, and tapping to them, you'd land late.",
            DelayPhase.WithOffsets =>
                $"{setup} With both, the visual offset brings the notes back, to be seen just as the beat is heard, and the audio offset still judges your tap from when you heard it: on the beat.",
            _ =>
                $"{setup} Without offsets, the sound and the picture reach you {Math.Abs(SoundHeard - PictureSeen):0} ms apart, and a tap on the beat you hear reaches the game {TapArrives:0} ms late."
        };
    }

    private static FlowContainer tabRow(ChoiceTab[] tabs) => new FlowContainer
    {
        AutoSizeAxes = Axes.Both,
        Direction = FlowDirection.Horizontal,
        Spacing = new Vector2(10, 0),
        Children = tabs
    };

    /// <summary>
    /// The beat's way, drawn as a chart along a line of time with the beat on it: a row each for the sound, the picture
    /// and the tap, each a bar from when it sets off to when it gets there, so the bar is the delay.
    /// </summary>
    private partial class Timeline : Container
    {
        // Room on the left for each row's name, and kept clear on the right for what's written past the last arrival.
        private const float names_width = 132;
        private const float end_room = 140;

        private const float axis_height = 26;
        private const float row_height = 56;
        private const float dot_size = 14;
        private const float bar_height = 6;

        // How far under its line what an offset moved is drawn.
        private const float shift_below = 18;

        // What's wrong, as a miss, dark enough to read on the light panel.
        private static readonly Color problem_color = JudgementColors.MISS.Darken(0.5f);

        // Each offset in its own color, wherever it moves something.
        private static readonly Color audio_offset_color = JudgementColors.CRITICAL;
        private static readonly Color visual_offset_color = JudgementColors.BREAK;

        private readonly DelayDiagram diagram;
        private readonly YuukiColorPalette palette;

        private readonly Lane sound;
        private readonly Lane picture;
        private readonly Lane tap;

        private readonly Box beatLine;
        private readonly SpriteText beatLabel;
        private readonly SpriteText[] tickLabels;
        private readonly Box gap;
        private readonly SpriteText gapLabel;
        private readonly SpriteText onTheBeat;

        public Timeline(DelayDiagram diagram, YuukiColorPalette palette)
        {
            this.diagram = diagram;
            this.palette = palette;

            RelativeSizeAxes = Axes.X;
            // With room under the last line for what an offset moved there.
            Height = axis_height + 3 * row_height + 10;

            Children = new Drawable[]
            {
                beatLine = new Box { Origin = Anchor.TopCentre, Width = 2, Height = 3 * row_height, Y = axis_height, Color = palette.Text, Alpha = 0.5f },
                beatLabel = label("the beat", palette.Text, Anchor.BottomCentre),
                gap = new Box { Origin = Anchor.CentreLeft, Height = 3, Y = axis_height + row_height, Color = problem_color },
                gapLabel = label(string.Empty, problem_color, Anchor.CentreLeft)
            };

            tickLabels = new[] { label("+100 ms", palette.Text, Anchor.BottomCentre), label("+200 ms", palette.Text, Anchor.BottomCentre) };

            foreach (var tick in tickLabels)
                tick.Alpha = 0.7f;

            AddRange(tickLabels);

            sound = new Lane(this, 0, IconUsage.MusicNote, "Sound", IconUsage.Hearing);
            picture = new Lane(this, 1, IconUsage.Smartphone, "Picture", IconUsage.Visibility);
            tap = new Lane(this, 2, IconUsage.TouchApp, "Your tap", null) { LabelColor = problem_color };

            Add(onTheBeat = label("on the beat", palette.Text, Anchor.CentreRight));
            onTheBeat.Y = rowY(2);
        }

        private static SpriteText label(string text, Color color, Anchor origin) => new SpriteText
        {
            Origin = origin,
            Text = text,
            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 15, FontWeight.Bold),
            Color = color
        };

        private float x(double ms) => names_width + (float)((ms - span_start) / (span_end - span_start)) * Math.Max(0, DrawWidth - names_width - end_room);

        private static float rowY(int row) => axis_height + row * row_height + row_height / 2;

        public override void Update()
        {
            // Placed before the children update, so they're drawn where they are this frame.
            layOut(diagram.Time);
            base.Update();
        }

        private void layOut(double time)
        {
            // The audio offset judges the tap on the beat; with the visual offset too, the notes are seen with the sound.
            bool audioOffset = diagram.Phase != DelayPhase.WithoutOffsets;
            bool together = diagram.Phase == DelayPhase.WithOffsets;

            beatLine.X = x(0);
            beatLabel.Position = new Vector2(x(0), axis_height - 4);
            tickLabels[0].Position = new Vector2(x(100), axis_height - 4);
            tickLabels[1].Position = new Vector2(x(200), axis_height - 4);

            var (_, pictureDelay, tapDelay) = diagram.Delays;

            sound.Show(time, 0, diagram.SoundHeard, $"+{diagram.SoundHeard:0} ms");
            picture.Show(time, diagram.PictureDrawn, diagram.PictureSeen, together ? "seen as it's heard" : $"+{diagram.PictureSeen:0} ms");

            // Where the notes are drawn, and what moved them there: the audio offset alone moves them with the judging,
            // as late as a tap reaches the game; the visual offset brings them back from there.
            double movedByAudio = diagram.SoundHeard + tapDelay;

            if (diagram.Phase == DelayPhase.AudioOnly)
                picture.ShowShift(0, movedByAudio, 1, "moved by the audio offset", audio_offset_color);
            else if (together)
                picture.ShowShift(movedByAudio, diagram.PictureDrawn, 1, $"visual offset +{pictureDelay + tapDelay:0} ms", visual_offset_color);
            else
                picture.HideShift();

            // Until both offsets are on, how far apart they reach you, between their rows.
            double heard = diagram.SoundHeard;
            double seen = diagram.PictureSeen;
            double apart = Math.Abs(heard - seen);
            bool showGap = !together && time >= Math.Max(heard, seen) && apart >= 5;

            gap.Alpha = gapLabel.Alpha = showGap ? 1 : 0;
            gap.X = x(Math.Min(heard, seen));
            gap.Width = x(Math.Max(heard, seen)) - gap.X;
            gapLabel.Text = $"{apart:0} ms apart";
            gapLabel.Position = new Vector2(gap.X + gap.Width + 8, gap.Y);

            // The tap: from when the beat's heard to the game, then with the audio offset, back to the beat.
            double arrives = diagram.TapArrives;
            double shown = diagram.TapShownAt;
            bool goingBack = audioOffset && time > arrives + offset_wait;
            bool landed = audioOffset && time >= arrives + offset_wait + offset_travel;

            tap.Show(time, heard, arrives, audioOffset ? string.Empty : $"judged {arrives:0} ms late", shown);
            tap.DotColor = landed ? audio_offset_color : palette.Control;

            if (goingBack)
                tap.ShowShift(arrives, diagram.TapJudged, (arrives - shown) / (arrives - diagram.TapJudged), $"audio offset -{arrives:0} ms", audio_offset_color);
            else
                tap.HideShift();

            onTheBeat.Alpha = landed ? 1 : 0;
            onTheBeat.X = x(0) - dot_size;
        }

        /// <summary>
        /// A row: its name, its line of time, a bar from when the thing sets off to its dot on the way, where it's
        /// received, and under the line, what an offset moved.
        /// </summary>
        private sealed class Lane
        {
            private readonly Timeline timeline;
            private readonly float y;

            private readonly Box line;
            private readonly Box bar;
            private readonly Circle dot;
            private readonly IconSprite? arrival;
            private readonly SpriteText label;

            private readonly Box shift;
            private readonly Circle shiftEnd;
            private readonly SpriteText shiftLabel;

            public Color LabelColor
            {
                set => label.Color = value;
            }

            public Color DotColor
            {
                set => dot.Color = value;
            }

            public Lane(Timeline timeline, int row, IconUsage icon, string name, IconUsage? receivedBy)
            {
                this.timeline = timeline;
                y = rowY(row);

                timeline.AddRange(new Drawable[]
                {
                    new FlowContainer
                    {
                        Origin = Anchor.CentreLeft,
                        Y = y,
                        AutoSizeAxes = Axes.Both,
                        Direction = FlowDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            new IconSprite
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Icon = icon,
                                IconSize = 20,
                                Filled = true,
                                Color = timeline.palette.Control
                            },
                            new SpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = name,
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 17, FontWeight.Bold),
                                Color = timeline.palette.Text
                            }
                        }
                    },
                    line = new Box { Origin = Anchor.CentreLeft, Y = y, Height = 2, Color = timeline.palette.Text, Alpha = 0.2f },
                    bar = new Box { Origin = Anchor.CentreLeft, Y = y, Height = bar_height, Color = timeline.palette.Control, Alpha = 0.45f },
                    dot = new Circle { Origin = Anchor.Centre, Y = y, Size = new Vector2(dot_size), Color = timeline.palette.Control },
                    shift = new Box { Origin = Anchor.CentreLeft, Y = y + shift_below, Height = 4, Alpha = 0 },
                    shiftEnd = new Circle { Origin = Anchor.Centre, Y = y + shift_below, Size = new Vector2(10), Alpha = 0 },
                    shiftLabel = new SpriteText
                    {
                        Origin = Anchor.CentreLeft,
                        Y = y + shift_below,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 14, FontWeight.Bold),
                        Color = timeline.palette.Text,
                        Alpha = 0
                    }
                });

                if (receivedBy is IconUsage receiver)
                {
                    // On the line, where the dot ends up: it takes the dot's place there.
                    timeline.Add(arrival = new IconSprite
                    {
                        Origin = Anchor.Centre,
                        Y = y,
                        Icon = receiver,
                        IconSize = 22,
                        Filled = true,
                        Color = timeline.palette.Control
                    });
                }

                timeline.Add(label = new SpriteText
                {
                    Origin = Anchor.CentreLeft,
                    Y = y,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 15, FontWeight.Bold),
                    Color = timeline.palette.Text
                });
            }

            /// <summary>
            /// Where it is at <paramref name="time"/>: set off at <paramref name="from"/>, received at
            /// <paramref name="to"/>, and then, for a tap, judged at <paramref name="settled"/>.
            /// </summary>
            public void Show(double time, double from, double to, string arrived, double? settled = null)
            {
                line.X = timeline.x(span_start);
                line.Width = timeline.x(span_end) - line.X;

                double progress = to <= from ? 1 : Math.Clamp((time - from) / (to - from), 0, 1);
                bool setOffYet = time >= from;
                bool hasArrived = time >= to;

                // The bar grows behind the dot: as long as it's been on its way.
                bar.Alpha = setOffYet ? 0.45f : 0;
                bar.X = timeline.x(from);
                bar.Width = timeline.x(from + (to - from) * progress) - bar.X;

                dot.Alpha = setOffYet && !(hasArrived && arrival != null) ? 1 : 0;
                dot.X = timeline.x(hasArrived && settled is double s ? s : from + (to - from) * progress);

                if (arrival != null)
                {
                    // Faint where the dot's going, then in its place with a little pop.
                    arrival.X = timeline.x(to);
                    arrival.Alpha = hasArrived ? 1 : 0.2f;
                    arrival.Scale = new Vector2(hasArrived ? 1 + 0.4f * (float)Math.Max(0, 1 - (time - to) / 25) : 1);
                }

                label.Text = arrived;
                label.X = timeline.x(to) + (arrival != null ? arrival.IconSize / 2 + 6 : dot_size);
                label.Alpha = hasArrived ? 1 : 0;
            }

            /// <summary>
            /// An offset moving it, under its line: from <paramref name="from"/> towards <paramref name="to"/>, as far
            /// as <paramref name="moved"/> (0 to 1) has got, ending in a dot where it's moved to, and named past the
            /// right-hand end, clear of the beat's line.
            /// </summary>
            public void ShowShift(double from, double to, double moved, string name, Color color)
            {
                float start = timeline.x(from);
                float end = timeline.x(from + (to - from) * Math.Clamp(moved, 0, 1));

                shift.Alpha = shiftEnd.Alpha = shiftLabel.Alpha = 1;
                shift.Color = shiftEnd.Color = color;
                shift.X = Math.Min(start, end);
                shift.Width = Math.Abs(end - start);
                shiftEnd.X = end;

                shiftLabel.Text = name;
                shiftLabel.X = Math.Max(start, end) + 10;
            }

            public void HideShift() => shift.Alpha = shiftEnd.Alpha = shiftLabel.Alpha = 0;
        }
    }

    /// <summary>
    /// One of a few choices as a small sheared button: the picked one filled dark with light text, the rest light with
    /// dark text, brightening on hover so it reads as clickable.
    /// </summary>
    private partial class ChoiceTab : ClickableContainer
    {
        private const double fade_duration = 200;

        private readonly Box background;
        private readonly SpriteText label;
        private readonly YuukiColorPalette palette;

        private bool selected;
        private bool hovered;

        public bool Selected
        {
            get => selected;
            set
            {
                if (selected == value)
                    return;

                selected = value;
                updateState();
            }
        }

        public ChoiceTab(string text, YuukiColorPalette palette)
        {
            this.palette = palette;

            AutoSizeAxes = Axes.Both;

            Children = new Drawable[]
            {
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    Shear = UserInterfaceConstant.Shear,
                    CornerRadius = 5,
                    Child = background = new Box
                    {
                        RelativeSizeAxes = Axes.Both
                    }
                },
                label = new SpriteText
                {
                    Margin = new MarginPadding { Left = 20, Right = 20, Top = 5, Bottom = 6 },
                    Text = text,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 18, FontWeight.Bold)
                }
            };

            updateState();
            FinishTransforms(true);

            Add(new HoverClickSounds(UISound.TabSelect));
        }

        public override bool OnHover(MouseEvent e)
        {
            hovered = true;
            updateState();
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            hovered = false;
            updateState();
            return base.OnHoverLost(e);
        }

        private void updateState()
        {
            Color fill = selected ? palette.Control : palette.Surface.Lighten(hovered ? 0.4f : 0.2f);

            background.FadeToColor(fill, fade_duration, Easing.OutQuint);
            background.FadeTo(selected || hovered ? 1f : 0.8f, fade_duration, Easing.OutQuint);
            label.FadeToColor(selected ? Color.White : palette.Text, fade_duration, Easing.OutQuint);
        }
    }
}
