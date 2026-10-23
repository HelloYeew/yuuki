// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
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
using Sakura.Framework.Platform;
using Yuuki.Game.Audio;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Graphics.Screens;

/// <summary>
/// How offsets work, from the short of it down: what Sakura measures and takes off by itself, the three offsets the
/// player sets for the rest, the player's own added up, which one to change for what's noticed, why sound and picture
/// each have one, and tips.
/// </summary>
public partial class OffsetGuideScreen : YuukiScreen, IHaveScreenAction
{
    // The page's widest, read comfortably; past it, the rest is margin.
    private const float max_content_width = 980;
    private const float side_margin = 32;

    // Kept clear under the page for the buttons along the bottom.
    private const float bottom_room = 110;

    // Between the title and the page.
    private const float title_gap = 12;

    public IReadOnlyList<ScreenAction> ScreenActions { get; }

    public override YuukiScreenPart ScreenPart => YuukiScreenPart.Play;

    public override bool DimBackground => true;

    /// <summary>
    /// Whether leaving opens the settings again, for a screen opened from there.
    /// </summary>
    public bool ReturnsToSettings { get; init; }

    /// <summary>
    /// The beat on its way.
    /// </summary>
    public DelayDiagram Diagram { get; private set; } = null!;

    /// <summary>
    /// What the player's own offsets say about their setup.
    /// </summary>
    public string YoursText => yoursText.Text;

    /// <summary>
    /// The player's offsets added up, with what Sakura measures (<see cref="OffsetStack.Summary"/>).
    /// </summary>
    public string StackSummary => stack.Summary;

    /// <summary>
    /// What Sakura measures of the sound's and the picture's delays now, as shown ("20 ms now"), and what that means.
    /// </summary>
    public string SoundMeasuredText => soundFigure.Text;

    public string PictureMeasuredText => pictureFigure.Text;

    public string SoundNote { get; private set; } = string.Empty;

    public string PictureNote { get; private set; } = string.Empty;

    /// <summary>
    /// The sound's and the picture's delays Sakura measures (ms), read from it twice a second. Set, they're taken as
    /// given instead (tests).
    /// </summary>
    public double? SoundDelay { get; set; }

    public double? PictureDelay { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private OffsetManager? offsets { get; set; }

    [Resolved(canBeNull: true)]
    private SettingsOverlay? settings { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiScreenStack? screenStack { get; set; }

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    [Resolved(canBeNull: true)]
    private IAudioManager? audioManager { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiAudioManager? music { get; set; }

    [Resolved(canBeNull: true)]
    private AppHost? host { get; set; }

    // The offsets as they are, and the delays as last measured, for what they say together.
    private int audioOffset;
    private int visualOffset;
    private double measuredSound = double.NaN;
    private double measuredPicture = double.NaN;
    private string? measuredDevices;
    private double nextMeasurement;

    // Read this often: the figures settle over seconds, and don't need every frame.
    private const double measurement_interval = 500;

    private YuukiColorPalette palette;

    private FlowContainer page = null!;
    private Container scrollArea = null!;
    private ScreenTitleTab titleTab = null!;
    private OffsetStack stack = null!;
    private SpriteText yoursText = null!;

    private SpriteText soundFigure = null!;
    private TextFlowContainer soundNote = null!;
    private SpriteText pictureFigure = null!;
    private TextFlowContainer pictureNote = null!;
    private SpriteText devicesFigure = null!;

    private LeftBottomShearButton backButton = null!;
    private RightBottomShearButton calibrateButton = null!;

    public OffsetGuideScreen()
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
        ScreenName.Value = "Offsets";
        ScreenHelperText.Value = "What Sakura measures by itself, what you set, and which one to change.";

        palette = Palette;

        AddRange(new Drawable[]
        {
            // Under the title, so nothing scrolls up behind it.
            scrollArea = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Child = new YuukiScrollableContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = page = new FlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FlowDirection.Vertical,
                        Spacing = new Vector2(0, 20),
                        Children = new Drawable[]
                        {
                            card("In short", paragraph("Everything takes a moment to reach you: the sound to your ears, the picture to your eyes, "
                                                       + "and your tap back to the game. Sakura, the engine the game runs on, measures what it can "
                                                       + "of that and takes it off by itself. Your offsets cover the rest. Calibrating sets them in "
                                                       + "about a minute, on each pair of headphones and each screen you play on.")),
                            card("What Sakura does by itself", createSakura()),
                            card("What you set", column(16,
                                tip(IconUsage.Headphones, "Audio offset · each audio device",
                                    "When your taps count: for what Sakura can't measure of the sound's delay, and your tap's own. Speakers, "
                                    + "wired and Bluetooth headphones each keep their own. Changes your score."),
                                tip(IconUsage.Monitor, "Visual offset · each screen",
                                    "Where the notes are drawn: for what Sakura can't measure of the picture's delay. Each screen keeps its "
                                    + "own, a TV most of all. Never changes your score, as taps are judged the same."),
                                tip(IconUsage.MusicNote, "Song offset · each difficulty",
                                    "For a song out of time with its own music: moves its notes and their judging together, on top of the "
                                    + "other two. Set it before playing, nudge it while playing, or take it from your last play."))),
                            card("Yours, added up", createYours()),
                            card("Which one do I change?", createWhichOne()),
                            card("Why sound and picture each have one", column(14,
                                paragraph("One offset can make taps on the music count on the beat, but it moves the notes with them, so they no "
                                          + "longer meet the line with the music. The second puts the notes right. Here's a beat on its way to "
                                          + "you, slowed down:"),
                                Diagram = new DelayDiagram())),
                            card("Getting the most from them", column(16, tips()))
                        }
                    }
                }
            },
            titleTab = new ScreenTitleTab("Offsets", palette),
            backButton = new LeftBottomShearButton
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Position = LeftBottomShearButton.DEFAULT_POSITION,
                Action = Exit,
                BackgroundColor = palette.Surface
            },
            calibrateButton = new RightBottomShearButton
            {
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = RightBottomShearButton.DEFAULT_POSITION,
                Text = "Calibrate",
                Icon = IconUsage.Tune,
                Action = Calibrate,
                BackgroundColor = palette.Surface
            }
        });
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (config == null)
        {
            updateYours();
            return;
        }

        var audio = config.Get<int>(YuukiSetting.AudioOffset);
        var visual = config.Get<int>(YuukiSetting.VisualOffset);

        // Back from calibrating, or changed in the settings, they're shown as they are.
        BindValueChanged(audio, e =>
        {
            audioOffset = e.NewValue;
            updateYours();
        }, true);
        BindValueChanged(visual, e =>
        {
            visualOffset = e.NewValue;
            updateYours();
        }, true);
    }

    public override void OnEntering(Screen? last)
    {
        base.OnEntering(last);

        Alpha = 0;
        this.FadeIn(300, Easing.OutQuint);
        titleTab.SlideIn();
    }

    public override void OnExiting(Screen? next)
    {
        base.OnExiting(next);

        this.FadeOut(300, Easing.OutQuint);
        titleTab.SlideOut();

        if (ReturnsToSettings)
            settings?.Show();
    }

    public override void OnSuspending(Screen next)
    {
        base.OnSuspending(next);
        this.FadeOut(300, Easing.OutQuint);
        titleTab.SlideOut();
    }

    public override void OnResuming(Screen? last)
    {
        base.OnResuming(last);
        this.FadeIn(300, Easing.OutQuint);
        titleTab.SlideIn();
    }

    public override void Update()
    {
        base.Update();

        // Centred, and no wider than reads well; below the title.
        float side = Math.Max(side_margin, (DrawWidth - max_content_width) / 2);
        scrollArea.Padding = new MarginPadding { Top = titleTab.Bottom + title_gap };
        page.Padding = new MarginPadding { Left = side, Right = side, Top = 4, Bottom = bottom_room };

        // What it's told to take as measured, so is the stack.
        stack.SoundDelay = SoundDelay;
        stack.PictureDelay = PictureDelay;

        if (Clock.CurrentTime < nextMeasurement)
            return;

        nextMeasurement = Clock.CurrentTime + measurement_interval;

        double sound = SoundDelay is double givenSound ? OffsetStack.Measured(givenSound) : OffsetStack.MeasuredSound(audioManager);
        double picture = PictureDelay is double givenPicture ? OffsetStack.Measured(givenPicture) : OffsetStack.MeasuredPicture(host);
        string? devices = offsets != null
            ? $"{OffsetManager.AudioDeviceLabel(offsets.AudioDevice)}, {OffsetManager.DisplayLabel(offsets.Display)}"
            : null;

        if (sound.Equals(measuredSound) && picture.Equals(measuredPicture) && devices == measuredDevices)
            return;

        measuredSound = sound;
        measuredPicture = picture;
        measuredDevices = devices;
        updateYours();
    }

    /// <summary>
    /// Calibrates, coming back here after.
    /// </summary>
    public void Calibrate()
    {
        calibrateButton.Flash(Color.White);

        // The clicks are all there is to hear: opened from the results, a song still playing out or their music
        // would be heard over them. The results start their music again, back there.
        music?.EndResultsMusic();
        music?.GameplayChannel?.Stop();

        screenStack?.Push(new CalibrationScreen());
    }

    /// <summary>
    /// What Sakura measures and the player's two offsets, and what they say together. Sakura takes off what it
    /// measures of the sound's delay and the picture's; the audio offset makes up for the rest of the sound's and the
    /// tap's, the visual offset for the rest of the picture's and the tap's. So with both calibrated, the sound reaches
    /// the player after the picture by what's left of the one offset against the other, give or take what's measured
    /// of each.
    /// </summary>
    private void updateYours()
    {
        int audio = audioOffset;
        int visual = visualOffset;

        updateSakura();

        int soundAfterPicture = (int)Math.Round(-audio - visual + orZero(measuredSound) - orZero(measuredPicture));

        yoursText.Text = audio == 0 && visual == 0
            ? "Not calibrated yet. Calibrating finds both in about a minute."
            : Math.Abs(soundAfterPicture) < 5
                ? "So the sound and the picture reach you together."
                : soundAfterPicture > 0
                    ? $"So the sound reaches you about {soundAfterPicture} ms after the picture."
                    : $"So the picture reaches you about {-soundAfterPicture} ms after the sound.";
    }

    #region What Sakura does

    /// <summary>
    /// What Sakura keeps track of by itself, with what it measures now: the sound's delay, the picture's, and which
    /// headphones and screen are played on; then what's left for the offsets.
    /// </summary>
    private Drawable createSakura() => column(16,
        paragraph("While you play, Sakura keeps track of these by itself, so your offsets don't have to:"),
        measuredTip(IconUsage.GraphicEq, "The sound's delay", soundFigure = figure(), soundNote = paragraph(string.Empty)),
        measuredTip(IconUsage.Monitor, "The picture's delay", pictureFigure = figure(), pictureNote = paragraph(string.Empty)),
        measuredTip(IconUsage.Devices, "Your headphones and screen", devicesFigure = figure(),
            paragraph("Sakura names the audio output and the screen the game is on. So the game can switch to each one's offsets as you "
                      + "change them, and tell you when it meets one it hasn't played on.")),
        note("What can't be measured from inside the game: how long your tap takes to arrive (keyboard, touchscreen), and delay the "
             + "hardware keeps to itself, such as Bluetooth headphones' radio or a TV's picture processing. That's what your offsets are for."));

    /// <summary>
    /// What Sakura measures now, and what that means for each offset.
    /// </summary>
    private void updateSakura()
    {
        soundFigure.Text = double.IsNaN(measuredSound) ? "not measured here" : $"{measuredSound:0} ms now";
        pictureFigure.Text = double.IsNaN(measuredPicture) ? "not measured here" : $"{measuredPicture:0} ms now";
        devicesFigure.Text = measuredDevices ?? string.Empty;

        string renderer = rendererName;

        SoundNote = double.IsNaN(measuredSound)
            ? "This audio output doesn't say how long it holds sound back, so here your audio offset covers all of the sound's delay."
            : "How long sound waits on its way to your headphones or speakers, in the audio buffers and the system's output. Sakura keeps "
              + "the song's clock on what you hear rather than what's been sent out, so taps are judged against the music as it reaches you.";

        PictureNote = double.IsNaN(measuredPicture)
            ? $"{char.ToUpperInvariant(renderer[0])}{renderer[1..]} can't tell when its frames are shown here, so your visual offset covers all of the picture's delay."
            : $"How long a frame takes from being made to being on the screen, timed frame by frame by {renderer}. The notes are drawn that "
              + "far ahead, so they're seen where they should be.";

        soundNote.Text = SoundNote;
        pictureNote.Text = PictureNote;
    }

    // The renderer by the name the settings give it, for where the picture's delay comes from.
    private string rendererName => host?.Renderer?.GetType().Name switch
    {
        "VulkanRenderer" => "the Vulkan renderer",
        "MetalRenderer" => "the Metal renderer",
        "GLRenderer" => "the OpenGL renderer",
        "D3D11Renderer" => "the Direct3D 11 renderer",
        _ => "this renderer"
    };

    /// <summary>
    /// A tip with what's measured beside its title.
    /// </summary>
    private Drawable measuredTip(IconUsage icon, string title, SpriteText measured, Drawable body) => tip(icon, new FlowContainer
    {
        AutoSizeAxes = Axes.Both,
        Direction = FlowDirection.Horizontal,
        Spacing = new Vector2(12, 0),
        Children = new Drawable[]
        {
            new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = title,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 19, FontWeight.Bold),
                Color = palette.Text
            },
            measured
        }
    }, body);

    private SpriteText figure() => new SpriteText
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 17, FontWeight.Bold),
        Color = palette.Control
    };

    #endregion

    private Drawable createYours() => column(14,
        stack = new OffsetStack(palette) { RelativeSizeAxes = Axes.X },
        yoursText = new SpriteText
        {
            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, bodySize, FontWeight.Regular),
            Color = palette.Text
        });

    /// <summary>
    /// What a player notices, and the offset that's for: the same choice the results screen makes for them after a play.
    /// </summary>
    private Drawable createWhichOne() => column(10,
        whichRow(heading("You notice"), heading("Change"), heading("The easy way")),
        whichRow(cell("Every song feels late (or early), whatever you play on"), cell("every device's offsets", bold: true),
            cell("Shift timing, after a play")),
        whichRow(cell("Only with these headphones or speakers"), cell("their audio offset", bold: true),
            cell("Calibrate while using them")),
        whichRow(cell("Notes look off the music, only on this screen"), cell("its visual offset", bold: true),
            cell("Calibrate on it")),
        whichRow(cell("Only this song"), cell("its song offset", bold: true),
            cell("Use last play before it, or nudge it while playing")));

    // The table's columns: what's noticed, what to change, how. A little short of the whole, as a flow wraps what doesn't fit.
    private static readonly float[] which_columns = { 0.4f, 0.25f, 0.33f };

    private Drawable whichRow(params Drawable[] cells)
    {
        var row = new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FlowDirection.Horizontal
        };

        for (int i = 0; i < cells.Length; i++)
        {
            row.Add(new Container
            {
                RelativeSizeAxes = Axes.X,
                Width = which_columns[i],
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Right = 12 },
                Child = cells[i]
            });
        }

        return row;
    }

    private Drawable cell(string text, bool bold = false) => new TextFlowContainer(t =>
    {
        t.Font = YuukiFontUsage.GetFontUsage(bold ? Typeface.Quicksand : Typeface.Nunito, bodySize, bold ? FontWeight.Bold : FontWeight.Regular);
        t.Color = bold ? palette.Control : palette.Text;
    })
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Text = text
    };

    private static double orZero(double milliseconds) => double.IsNaN(milliseconds) ? 0 : milliseconds;

    private SpriteText heading(string text) => new SpriteText
    {
        Text = text,
        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 14, FontWeight.Bold),
        Color = palette.Text,
        Alpha = 0.7f
    };

    private Drawable[] tips()
    {
        var tips = new List<Drawable>
        {
            tip(IconUsage.CheckCircle, "Let the results screen tell you",
                "It looks at your last few plays, not just one, and offers to change the offset that's off, saying which: every "
                + "device's, or just the song's.")
        };

        // A phone's game always has the screen: it has no display of its own to take.
        if (!RuntimeInfo.IsMobile)
        {
            tips.Add(tip(IconUsage.Fullscreen, "Play in Fullscreen",
                "Windowed and Borderless, the system puts the game together with the rest of the desktop before it's shown, "
                + "which can hold the picture back a frame or more. Fullscreen gives the game the display. Calibrate in the mode you play in."));
        }

        tips.Add(tip(IconUsage.Speed, "Short delays are steady delays",
            "Wired headphones, a TV's game mode and a high refresh rate leave less to correct, and it wavers less from tap to tap."));

        return tips.ToArray();
    }

    private static FlowContainer column(float spacing, params Drawable[] children) => new FlowContainer
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Direction = FlowDirection.Vertical,
        Spacing = new Vector2(0, spacing),
        Children = children
    };

    private TextFlowContainer paragraph(string text) => new TextFlowContainer(t =>
    {
        t.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, bodySize, FontWeight.Regular);
        t.Color = palette.Text;
    })
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Text = text
    };

    private TextFlowContainer note(string text) => new TextFlowContainer(t =>
    {
        t.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, gameLayout?.IsPhone == true ? 17 : 14, FontWeight.Regular);
        t.Color = palette.Text;
        t.Alpha = 0.8f;
    })
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Text = text
    };

    private Drawable tip(IconUsage icon, string title, string body) => tip(icon, new SpriteText
    {
        Text = title,
        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 19, FontWeight.Bold),
        Color = palette.Text
    }, paragraph(body));

    private Drawable tip(IconUsage icon, Drawable title, Drawable body) => new Container
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Children = new Drawable[]
        {
            new IconSprite
            {
                Icon = icon,
                IconSize = 26,
                Filled = true,
                Color = palette.Control
            },
            new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Padding = new MarginPadding { Left = 42 },
                Children = new[] { title, body }
            }
        }
    };

    /// <summary>
    /// A section of the page: a light panel with its title, as the setup screen's.
    /// </summary>
    private Drawable card(string title, Drawable content) => new Container
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
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
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 14),
                Padding = new MarginPadding { Left = 28, Right = 28, Top = 20, Bottom = 24 },
                Children = new[]
                {
                    new SpriteText
                    {
                        Text = title,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 26, FontWeight.Bold),
                        Color = palette.Text
                    },
                    content
                }
            }
        }
    };

    // Bigger on a phone, where the desktop size comes out too small to read.
    private int bodySize => gameLayout?.IsPhone == true ? 19 : 16;
}
