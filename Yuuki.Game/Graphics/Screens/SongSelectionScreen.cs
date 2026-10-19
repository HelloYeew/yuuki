// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Screens;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Platform.Dialogs;
using Sakura.Framework.Reactive;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;
using Yuuki.Game.Edit;
using Yuuki.Game.Edit.Components;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Notifications;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Graphics.Screens;

public partial class SongSelectionScreen : YuukiScreen, IHaveScreenAction
{
    public IReadOnlyList<ScreenAction> ScreenActions { get; }

    public override YuukiScreenPart ScreenPart => EditMode ? YuukiScreenPart.Edit : YuukiScreenPart.Play;

    /// <summary>
    /// Picking a difficulty to edit rather than to play (the main menu's Edit): Go opens the editor.
    /// </summary>
    public bool EditMode { get; init; }

    private Container content = null!;

    private BeatmapInfoPanel infoPanel = null!;
    private LeftBottomShearButton backButton = null!;

    private BeatmapSelector selector = null!;

    /// <summary>The carousel cluster, with its search and filters.</summary>
    public BeatmapSelector Selector => selector;
    private RightBottomShearButton goButton = null!;

    // Edit mode only: makes a new set from a song, beside Go.
    private RightBottomShearButton? newButton;
    private Container? newButtonHolder;

    private const double peek_hold_duration = 300;
    private const float peek_cancel_travel = 20f;
    private const double peek_fade_duration = 300;

    private double? pressStartTime;
    private Vector2 pressStartPosition;
    private bool peeking;

    /// <summary>
    /// Whether a held press is showing the background on its own (diagnostics/tests).
    /// </summary>
    public bool IsPeeking => peeking;

    public override bool HideVideo => false;

    public override bool LoopPreview => true;

    public override bool AllowBackgroundBlur => true;

    public SongSelectionScreen()
    {
        ScreenActions = new List<ScreenAction>()
        {
            new ScreenAction()
            {
                Name = "Back",
                Action = onBackButtonClick,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.Escape))
            },
            new ScreenAction()
            {
                Name = "Random",
                Action = onRandomClick,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.F2))
            }
        };
    }

    [Resolved(canBeNull: true)]
    private BeatmapsCollection? beatmapsCollection { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiGame? game { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiScreenStack? screenStack { get; set; }

    [Resolved(canBeNull: true)]
    private ScoreStore? scoreStore { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiBackground? background { get; set; }

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapWriter? writer { get; set; }

    [Resolved(canBeNull: true)]
    private AppHost? host { get; set; }

    [Resolved(canBeNull: true)]
    private NotificationOverlay? notifications { get; set; }

    private Reactive<bool>? useUnicodeInfo;

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        ScreenName.Value = EditMode ? "Edit" : "Solo Play";
        ScreenHelperText.Value = selectSongText;

        Add(content = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        });

        content.Add(infoPanel = new BeatmapInfoPanel(EditMode ? "Edit" : "Solo Play", Palette)
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(-30, 48)
        });
        infoPanel.ScoreSelected += score => screenStack?.Push(ScoreScreen.ForSavedScore(score, scoreStore));
        // The Ranking tab reaches down to the back button, or to the strip when that's higher.
        infoPanel.RankingBottom = () => Math.Min(backButton.DrawRectangle.Y, selector.StripTop);
        infoPanel.RankingMaxRight = () => selector.SideAreaLeft;

        content.Add(backButton = new LeftBottomShearButton()
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Position = new Vector2(-200, LeftBottomShearButton.DEFAULT_POSITION.Y),
            Alpha = 0,
            Action = onBackButtonClick,
            BackgroundColor = Palette.Surface,
        });

        content.Add(selector = new BeatmapSelector());

        selector.SetChanged += onSelectorSetChanged;
        selector.BeatmapChanged += onSelectorBeatmapChanged;
        selector.ListChanged += onSelectorListChanged;
        selector.StateChanged += onSelectorStateChanged;
        selector.SelectedCardClicked += onGoClick;

        content.Add(goButton = new RightBottomShearButton()
        {
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomRight,
            Position = new Vector2(200, RightBottomShearButton.DEFAULT_POSITION.Y),
            Alpha = 0,
            Text = EditMode ? "Edit" : "Next",
            Icon = EditMode ? IconUsage.Edit : IconUsage.ArrowRightAlt,
            Action = onGoClick,
            BackgroundColor = Palette.Surface,
        });

        if (EditMode)
        {
            // Beside Go, one button's width to its left. Held in a shifted container rather than placed
            // there, since the button's hover nudge moves it to the corner position.
            content.Add(newButtonHolder = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                X = -250,
                Child = newButton = new RightBottomShearButton
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    Position = new Vector2(200, RightBottomShearButton.DEFAULT_POSITION.Y),
                    Alpha = 0,
                    Text = "New",
                    Icon = IconUsage.Add,
                    Action = onNewBeatmapClick,
                    BackgroundColor = Palette.Accent,
                    // Only the way forward (Edit) keeps the beat; this one stays still beside it.
                    FlashToBeat = false
                }
            });
        }

        if (config != null)
        {
            useUnicodeInfo = config.Get<bool>(YuukiSetting.UseUnicodeInfo);
            // The info panel follows the setting itself; this keeps the helper text in step.
            // Tracked, so this screen is released when it exits: the setting is a single shared instance
            // that lives as long as the config manager does.
            BindValueChanged(useUnicodeInfo, onUseUnicodeInfoChanged);
        }
    }

    private void onSelectorSetChanged(BeatmapSet? set)
    {
        if (set == null)
            return;

        updateInfoPanel();
        updateGoButtonState(set);
        ScreenHelperText.Value = helperTextFor(set);
    }

    // The list emptied (or filled again): with nothing listed there's nothing for Go to pick.
    private void onSelectorListChanged() => updateGoButtonState(selector.CurrentSet);

    private void onSelectorBeatmapChanged(Beatmap? beatmap)
    {
        if (beatmap == null)
            return;

        updateInfoPanel();
    }

    private void onSelectorStateChanged(BeatmapSelector.SelectionState state)
    {
        ScreenHelperText.Value = state == BeatmapSelector.SelectionState.SelectingBeatmap
            ? EditMode ? "Select a difficulty to edit!" : "Select a difficulty!"
            : selectSongText;

        updateInfoPanel();
    }

    /// <summary>
    /// Song view while picking a song; the full difficulty view while picking a difficulty. The beatmap
    /// is only trusted once it belongs to the current set — right after a set changes, the working
    /// beatmap can briefly still be a difficulty of the previous one.
    /// </summary>
    private void updateInfoPanel()
    {
        BeatmapSet? set = selector.CurrentSet;
        Beatmap? beatmap = selector.CurrentBeatmap;

        if (selector.State == BeatmapSelector.SelectionState.SelectingBeatmap && beatmap?.BeatmapSet?.ID == set?.ID && beatmap != null)
            infoPanel.ShowBeatmap(beatmap);
        else
            infoPanel.ShowSet(set);
    }

    private string helperTextFor(BeatmapSet set)
    {
        bool useUnicode = !useUnicodeInfo?.Value ?? false;
        return $"{set.GetDisplayArtist(useUnicode)} - {set.GetDisplayTitle(useUnicode)} ({set.GetDisplaySource(useUnicode)})";
    }

    private void onUseUnicodeInfoChanged(ValueChangedEvent<bool> e)
    {
        var set = selector.CurrentSet;
        if (set == null)
            return;

        ScreenHelperText.Value = helperTextFor(set);
    }

    private void updateGoButtonState(BeatmapSet? set)
    {
        if (goButton == null)
            return;

        bool hasBeatmaps = set != null
                           && selector.HasSongsListed
                           && beatmapsCollection != null
                           && beatmapsCollection.GetBeatmapsFromBeatmapSet(set).Length > 0;

        goButton.Enabled.Value = hasBeatmaps;
        goButton.FadeTo(hasBeatmaps ? 1f : 0.4f, 200, Easing.OutQuint);
    }

    public override void Update()
    {
        base.Update();

        if (pressStartTime != null && !peeking && Clock.CurrentTime - pressStartTime >= peek_hold_duration)
            setPeek(true);
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        bool handled = base.OnMouseDown(e);

        if (State == ScreenState.Active && e.Button == MouseButton.Left)
        {
            pressStartTime = Clock.CurrentTime;
            pressStartPosition = e.ScreenSpaceMousePosition;
            return true;
        }

        return handled;
    }

    public override bool OnMouseMove(MouseEvent e)
    {
        if (pressStartTime != null && !peeking)
        {
            float dx = e.ScreenSpaceMousePosition.X - pressStartPosition.X;
            float dy = e.ScreenSpaceMousePosition.Y - pressStartPosition.Y;
            if (dx * dx + dy * dy > peek_cancel_travel * peek_cancel_travel)
                pressStartTime = null;
        }

        return base.OnMouseMove(e);
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        if (e.Button == MouseButton.Left)
        {
            pressStartTime = null;
            setPeek(false);
        }

        return base.OnMouseUp(e);
    }

    private void setPeek(bool on)
    {
        if (peeking == on)
            return;

        peeking = on;

        // Cleared before re-applying because the framework retargets an in-flight transform in place,
        // keeping its original StartValue as well as its EndTime. Reversing a peek before the fade has
        // finished would therefore hand the transform the same value at both ends and whatever was left
        // of the original window, so it snaps instead of animating back — releasing the button early
        // looked like the animation simply did not play. A cleared slot gets a fresh transform that
        // captures the current alpha and scale, so the reverse runs its full duration from wherever it
        // was interrupted. Only setPeek transforms this container, so nothing else is discarded.
        content.ClearTransforms();

        content.FadeTo(on ? 0f : 1f, peek_fade_duration, Easing.OutQuint)
            .ScaleTo(on ? 1.5f : 1f, peek_fade_duration, Easing.OutQuint);
        game?.ShowFooter.Value = !on;
        // Clear the blur while peeking so the revealed background is sharp.
        background?.SetBlurSuppressed(on);
        // And tuck the cursor away, so nothing at all sits over the background.
        CursorHidden.Value = on;
    }

    private string selectSongText => EditMode ? "Select a song to edit, or make a new one!" : "Select a song to play!";

    private void onNewBeatmapClick()
    {
        newButton?.Flash(Color.White);

        host?.ShowOpenFileDialog(new FileDialogOptions
        {
            Title = "Choose the song for your new beatmap",
            Filters = new[] { new FileDialogFilter("Audio", BeatmapCreator.AUDIO_EXTENSIONS) }
        }, result =>
        {
            if (result.Successful && result.Path != null)
                CreateBeatmapFrom(result.Path);
        });
    }

    /// <summary>
    /// Makes a new set around the song at <paramref name="audioPath"/> and opens the editor on it.
    /// Leaving without saving throws it away again.
    /// </summary>
    public void CreateBeatmapFrom(string audioPath)
    {
        if (writer == null || beatmapsCollection == null || workingBeatmap == null || screenStack == null || !IsCurrentScreen)
            return;

        Beatmap created;

        try
        {
            created = BeatmapCreator.CreateSet(audioPath, writer, beatmapsCollection);
        }
        catch (Exception e)
        {
            Logger.Error(e);
            notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't make a beatmap from that song: {e.Message}" });
            return;
        }

        var restore = (workingBeatmap.BeatmapSet, workingBeatmap.Beatmap);

        // Only the difficulty: making the new set the working one would start its preview first.
        workingBeatmap.Beatmap = created;

        screenStack.Push(new EditorScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            InitialMode = EditorScreenMode.Setup,
            IsNewBeatmap = true,
            RestoreOnDiscard = restore
        });
    }

    private void onGoClick()
    {
        if (selector.State == BeatmapSelector.SelectionState.SelectingSet)
        {
            selector.EnterDifficultySelection();
        }
        else if (EditMode)
        {
            goButton.Flash(Color.White);
            screenStack?.Push(new EditorScreen
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both
            });
        }
        else
        {
            goButton.Flash(Color.White);
            screenStack?.Push(new FinalSetupScreen
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both
            });
        }
    }

    public override void OnEntering(Screen? last)
    {
        base.OnEntering(last);
        selector.ResetToSets();
        playShowAnimation();
    }

    public override void OnResuming(Screen? last)
    {
        base.OnResuming(last);

        // Passing through: one difficulty's editor handing over to another's, straight back on top.
        if (last is EditorScreen { SwitchingDifficulty: true })
            return;

        // Back from the editor, which may have saved (or made a set): list the library as it is now.
        if (last is EditorScreen)
            selector.RefreshFromLibrary();

        playShowAnimation();
    }

    public override void OnSuspending(Screen next)
    {
        base.OnSuspending(next);
        playHideAnimation();
    }

    public override void OnExiting(Screen? next)
    {
        base.OnExiting(next);
        playHideAnimation();

        if (useUnicodeInfo != null)
            useUnicodeInfo.ValueChanged -= onUseUnicodeInfoChanged;
    }

    public override bool OnScroll(ScrollEvent e)
    {
        // Route wheel input to the selector.
        if (e.ScrollDelta.Y < 0)
            selector.NavigateNext();
        else if (e.ScrollDelta.Y > 0)
            selector.NavigatePrevious();

        return true;
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        bool vertical = selector.Orientation == CarouselOrientation.Vertical;

        // While searching, Space is a space in the search, not "go" (the search box doesn't take it as
        // a key, only as typed text, so it would otherwise land here too).
        if (selector.IsSearchFocused && e.Key == Key.Space)
            return false;

        // Typing a letter or digit anywhere starts a search: the typed character follows
        // the key into the now-focused box.
        if (!selector.IsSearchFocused && selector.State == BeatmapSelector.SelectionState.SelectingSet && isSearchKey(e))
        {
            selector.FocusSearch();
            return false;
        }

        switch (e.Key)
        {
            // Navigation follows the carousel axis: up/down when vertical, left/right when horizontal.
            case Key.Up when vertical:
            case Key.Left when !vertical:
                selector.NavigatePrevious();
                return true;

            case Key.Down when vertical:
            case Key.Right when !vertical:
                selector.NavigateNext();
                return true;

            // One press, one step on: held, it would run from the song to its difficulties and on to
            // the next screen. Navigation above keeps its repeats, so holding an arrow scrolls.
            case Key.Enter when !e.IsRepeat:
            case Key.Space when !e.IsRepeat:
                onGoClick();
                return true;
        }

        return base.OnKeyDown(e);
    }

    private static bool isSearchKey(KeyEvent e)
    {
        if ((e.Modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0)
            return false;

        return e.Key is >= Key.A and <= Key.Z or >= Key.Number0 and <= Key.Number9;
    }

    private void playShowAnimation()
    {
        peeking = false;
        pressStartTime = null;
        content.Alpha = 1;
        background?.SetBlurSuppressed(false);
        CursorHidden.Value = false;

        selector.ShowRegion();

        updateInfoPanel();
        infoPanel.AnimateIn();
        backButton.MoveTo(LeftBottomShearButton.DEFAULT_POSITION, 500, Easing.OutQuint)
            .FadeIn(500, Easing.OutQuint);
        goButton.MoveTo(RightBottomShearButton.DEFAULT_POSITION, 500, Easing.OutQuint);
        newButton?.MoveTo(RightBottomShearButton.DEFAULT_POSITION, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        this.FadeIn(500, Easing.OutQuint);

        updateGoButtonState(selector.CurrentSet);
    }

    private void playHideAnimation()
    {
        selector.HideRegion();

        infoPanel.AnimateOut();
        backButton.MoveTo(new Vector2(-200, LeftBottomShearButton.DEFAULT_POSITION.Y), 500, Easing.OutQuint)
            .FadeOut(500, Easing.OutQuint);
        goButton.MoveTo(new Vector2(200, RightBottomShearButton.DEFAULT_POSITION.Y), 500, Easing.OutQuint)
            .FadeOut(500, Easing.OutQuint);
        newButton?.MoveTo(new Vector2(200, RightBottomShearButton.DEFAULT_POSITION.Y), 500, Easing.OutQuint)
            .FadeOut(500, Easing.OutQuint);
        this.FadeOut(500, Easing.OutQuint);
    }

    private void onBackButtonClick()
    {
        if (selector.State == BeatmapSelector.SelectionState.SelectingBeatmap)
        {
            selector.ExitToSets();
            return;
        }

        selector.SetChanged -= onSelectorSetChanged;
        selector.BeatmapChanged -= onSelectorBeatmapChanged;
        selector.ListChanged -= onSelectorListChanged;
        selector.StateChanged -= onSelectorStateChanged;
        selector.SelectedCardClicked -= onGoClick;

        Exit();
    }

    private void onRandomClick() => selector.SelectRandom();
}
