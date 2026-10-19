// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Screens;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Drawables;
using Yuuki.Game.Graphics.Screens;

namespace Yuuki.Game.Graphics.ScreenStacks;

public partial class YuukiScreenStack : ScreenStack
{
    [Resolved(canBeNull: true)]
    private ScreenFooter? footer { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiGame? game;

    [Resolved(canBeNull: true)]
    private IYuukiColorProvider? colorProvider { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiBackground? background { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiAudioManager? audioManager { get; set; }

    [Resolved(canBeNull: true)]
    private Cursor.YuukiCursorContainer? cursor { get; set; }

    /// <summary>
    /// The screen the shared footer/video/preview state was last synced to. Equals
    /// <see cref="ScreenStack.CurrentScreen"/> after the stack settles; exposed mainly so tests can
    /// assert an exit cascade doesn't leave a stale intermediate screen's state applied.
    /// </summary>
    public YuukiScreen? AppliedScreen { get; private set; }

    // The screen whose ScreenName/HelperText the footer is currently bound to, so we can unbind it
    // before rebinding to a new screen.
    private YuukiScreen? boundScreen;

    private bool applyScheduled;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    // The screens on the way to the current one, oldest first: a change to one of them is going back.
    private readonly List<Screen> history = new List<Screen>();

    /// <summary>
    /// Opens Solo Play's song select on <paramref name="set"/> (clicking a finished import does this): picks
    /// it there if song select is already up, else opens one over the menu. Only from the menus and song
    /// select. From setup, a song, its results, a replay or the editor it doesn't go, since that would end
    /// what's happening there.
    /// </summary>
    /// <returns>Whether it went, so a notification that asked can stay until it can.</returns>
    public bool PresentBeatmapSet(BeatmapSet set)
    {
        if (workingBeatmap == null || CurrentScreen is not (MainMenuScreen or PlayScreen or SongSelectionScreen))
            return false;

        // Song select follows the working beatmap, listing it even if a search would hide it.
        workingBeatmap.BeatmapSet = set;

        if (CurrentScreen is not SongSelectionScreen { EditMode: false })
        {
            Push(new SongSelectionScreen
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both
            });
        }

        return true;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        OnScreenChange += (_, next) =>
        {
            playTransitionSound(next);
            scheduleApply();
        };
    }

    /// <summary>
    /// A whoosh forward for a new screen, back for one returned to (none for the very first).
    /// </summary>
    private void playTransitionSound(Screen? next)
    {
        if (next == null)
            return;

        int index = history.IndexOf(next);

        if (index >= 0)
        {
            history.RemoveRange(index + 1, history.Count - index - 1);
            uiSounds?.Play(UISound.ScreenBack);
            return;
        }

        if (history.Count > 0)
            uiSounds?.Play(UISound.ScreenForward);

        history.Add(next);
    }

    /// <summary>
    /// Leaves screens until a <typeparamref name="T"/> is the current one,
    /// leaving everything as it is, when there's no
    /// <typeparamref name="T"/> to stop at.
    /// </summary>
    public bool ExitTo<T>() where T : Screen
    {
        if (Children.OfType<T>().All(screen => screen.Stack != this))
            return false;

        // Looked at afresh after every exit, since one can set off others: gameplay and its setup
        // screen leave by themselves when they're returned to.
        while (CurrentScreen is not null and not T)
            CurrentScreen.Exit();

        return CurrentScreen is T;
    }

    private void scheduleApply()
    {
        if (applyScheduled)
            return;

        applyScheduled = true;
        Scheduler.Add(applyCurrentScreenState);
    }

    private void applyCurrentScreenState()
    {
        applyScheduled = false;

        var current = CurrentScreen as YuukiScreen;

        if (boundScreen != null && boundScreen != current)
        {
            boundScreen.ScreenName.UnbindAll();
            boundScreen.ScreenHelperText.UnbindAll();
            boundScreen = null;
        }

        if (current == null)
            return;

        AppliedScreen = current;

        footer?.ScreenName.Value = current.ScreenName.Value;
        footer?.ScreenDescription.Value = current.ScreenHelperText.Value;
        footer?.ColorPalette.Value = colorProvider?.GetPalette(current.ScreenPart) ??
                                     DefaultYuukiColorProvider.GetDefaultPalette();

        // Rebind the live name/description feed, clearing any prior binding first so re-applying the
        // same screen (e.g. on resume) doesn't stack duplicate handlers.
        if (boundScreen != current)
        {
            current.ScreenName.UnbindAll();
            current.ScreenHelperText.UnbindAll();
            current.ScreenName.ValueChanged += name => footer?.ScreenName.Value = name.NewValue;
            current.ScreenHelperText.ValueChanged += helperText => footer?.ScreenDescription.Value = helperText.NewValue;
            boundScreen = current;
        }

        game?.ShowFooter.Value = !current.HideFooter;
        footer?.ScreenActions.Value = current is IHaveScreenAction haveScreenAction
            ? haveScreenAction.ScreenActions
            : new List<ScreenAction>();

        if (current.HideVideo)
            background?.HideVideo();
        else
            background?.ShowVideo();

        background?.SetGameplayDim(current.DimBackground);
        background?.SetBlurAllowed(current.AllowBackgroundBlur);

        audioManager?.LoopPreview.Value = current.LoopPreview;
        audioManager?.AllowTrackChange.Value = current.AllowTrackChange;

        // Follow the new screen's cursor wish; dropping the old binding releases the old screen.
        if (cursor != null)
        {
            cursor.Hidden.UnbindAll();
            cursor.Hidden.BindTo(current.CursorHidden);
        }
    }
}
