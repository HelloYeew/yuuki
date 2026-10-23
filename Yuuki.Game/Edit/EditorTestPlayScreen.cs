// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Screens;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Edit;

/// <summary>
/// editor test play (F5)
/// </summary>
public partial class EditorTestPlayScreen : YuukiScreen
{
    /// <summary>
    /// How far before the start the song picks up, so the first notes scroll in.
    /// </summary>
    private const double run_up = 1500;

    private readonly PlayableBeatmap chart;
    private readonly double startTime;
    private readonly IAudioChannel? channel;

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    private GameplayContainer gameplay = null!;
    private double endTime;
    private bool exiting;

    public override YuukiScreenPart ScreenPart => YuukiScreenPart.Play;

    public override bool HideFooter => true;

    public override bool DimBackground => true;

    public override bool AllowTrackChange => false;

    protected override bool ScalesWithUI => false;

    /// <param name="chart">The editor's chart, as gameplay plays it.</param>
    /// <param name="startTime">Where to start; notes before it are left out.</param>
    /// <param name="channel">The editor's song channel, borrowed. Null runs silent.</param>
    public EditorTestPlayScreen(PlayableBeatmap chart, double startTime, IAudioChannel? channel)
    {
        this.startTime = startTime;
        this.channel = channel;

        // From the start time on, leaves earlier objects out of a test.
        var notes = chart.Notes.Where(n => n.StartTime >= startTime - 1).ToArray();

        this.chart = new PlayableBeatmap(notes, chart.LaneCount, chart.Beatmap)
        {
            ControlPoints = chart.ControlPoints
        };
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        ScreenName.Value = "Test play";
        CursorHidden.Value = true;

        var palette = colorProvider?.GetPalette(YuukiScreenPart.Edit) ?? DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Edit);

        endTime = (chart.Notes.Length > 0 ? chart.LastNoteTime : startTime) + 1500;

        if (channel != null && channel.Length > 0)
            endTime = Math.Min(endTime, channel.Length);

        AddRange(new Drawable[]
        {
            gameplay = new GameplayContainer(chart, channel != null ? new TrackClock(channel) : null)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Preempt = GameplayScreen.ScrollPreempt(config, chart),
                LeadInTime = GameplayScreen.LeadInFor(chart),
                LaneKeys = LaneKeyLayouts.Parse(config?.Get<string>(YuukiConfigManager.LaneKeysSetting(chart.LaneCount)).Value, chart.LaneCount),
                SwipeLeftKey = config?.Get<Key>(YuukiSetting.SwipeLeftKey).Value,
                SwipeRightKey = config?.Get<Key>(YuukiSetting.SwipeRightKey).Value,
                AudioOffset = config?.Get(YuukiSetting.AudioOffset, 0).Value ?? 0,
                VisualOffset = config?.Get(YuukiSetting.VisualOffset, 0).Value ?? 0,
                SongLength = channel != null && channel.Length > 0 ? channel.Length : null,
                // By touch, the HUD's pause button: there's no pausing a test, so it goes back as Escape does.
                PauseRequested = leave
            },
            new TestPlayBadge(palette, gameLayout?.Touch.Value == true)
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Y = 20
            }
        });
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        gameplay.Restart();

        // Restart starts from the lead-in before 0; a test from later in the song starts a run-up before it.
        double from = startTime - run_up;

        if (from > -gameplay.LeadInTime)
            gameplay.Seek(from);
    }

    public override void OnEntering(Screen? last)
    {
        base.OnEntering(last);
        this.FadeInFromZero(300, Easing.OutQuint);
    }

    public override void OnExiting(Screen? next)
    {
        base.OnExiting(next);

        gameplay.Stop();
        this.FadeOut(200, Easing.OutQuint);
    }

    public override void Update()
    {
        base.Update();

        if (!exiting && (gameplay.AllJudged || gameplay.GameplayClock.CurrentTime >= endTime))
            leave();
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (e.Key == Key.Escape && !e.IsRepeat)
        {
            leave();
            return true;
        }

        return base.OnKeyDown(e);
    }

    private void leave()
    {
        if (exiting)
            return;

        exiting = true;
        Exit();
    }

    private partial class TestPlayBadge : Container
    {
        public TestPlayBadge(YuukiColorPalette palette, bool touch)
        {
            Masking = true;
            CornerRadius = 15;
            Height = 30;
            AutoSizeAxes = Axes.X;

            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One, Color = palette.Text.Darken(0.4f).WithAlpha(0.85f) },
                new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = touch ? "Test play! Tap pause to go back" : "Test play! Esc to go back",
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Bold),
                    Color = Color.White,
                    Margin = new MarginPadding { Left = 16, Right = 16 }
                }
            };
        }
    }
}
