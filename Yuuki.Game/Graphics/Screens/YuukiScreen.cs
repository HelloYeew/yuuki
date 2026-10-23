// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Screens;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Reactive;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.Screens;

public partial class YuukiScreen : Screen
{
    public readonly Reactive<string> ScreenName = new Reactive<string>("Screen");

    public readonly Reactive<string> ScreenHelperText = new Reactive<string>("Helper text");

    public virtual YuukiScreenPart ScreenPart => YuukiScreenPart.MainMenu;

    public virtual bool HideFooter => false;

    public virtual bool HideVideo => true;

    public virtual bool LoopPreview => false;

    /// <summary>
    /// Whether the background should be dimmed while this screen is active (gameplay only).
    /// </summary>
    public virtual bool DimBackground => false;

    /// <summary>
    /// Whether the song may be switched (music player previous / next / shuffle / list) on this screen.
    /// The player still opens everywhere to show what's playing, but the screens around a play (setup,
    /// gameplay, results) are tied to the song that's being played, so they lock switching.
    /// </summary>
    public virtual bool AllowTrackChange => true;

    /// <summary>
    /// Whether the cursor should be tucked away. A reactive rather than a fixed answer, because a
    /// screen can change its mind while showing — gameplay hides it while playing and brings it back
    /// for the pause menu. The screen stack binds the cursor to the current screen's.
    /// </summary>
    public readonly ReactiveBool CursorHidden = new ReactiveBool(false);

    /// <summary>
    /// Whether this screen honors the "blur background" setting. Only screens that opt in (song select)
    /// blur, so the setting has no effect elsewhere.
    /// </summary>
    public virtual bool AllowBackgroundBlur => false;

    /// <summary>
    /// Whether this screen follows the UI scale setting. Gameplay screens don't, the scale is
    /// for menus, and the playfield and its HUD keep the size they were tuned at.
    /// </summary>
    protected virtual bool ScalesWithUI => true;

    /// <summary>
    /// Asked when the game is about to back out through this screen on its way to closing (the window's
    /// close button returns to the main menu first). A screen with something to lose (the editor's
    /// unsaved changes) asks the player itself and returns true, then calls <paramref name="resume"/>
    /// once the exit may go on; false lets it go on now.
    /// </summary>
    public virtual bool InterceptGameExit(Action resume) => false;

    // For a screen that doesn't scale with the UI: its own canvas, which undoes the UI scale. 1920×1080, zoomed in
    // as the menus are in a phone's window (see GameLayout.GameplayCanvas).
    private readonly DrawSizePreservingFillContainer? fixedCanvas;

    protected override Container Content => (Container?)fixedCanvas ?? this;

    public YuukiScreen()
    {
        if (!ScalesWithUI)
            AddInternal(fixedCanvas = new DrawSizePreservingFillContainer { RelativeSizeAxes = Axes.Both });
    }

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    /// <summary>
    /// This screen's colors, from <see cref="ScreenPart"/>. Everything on the screen gets them too, as a
    /// <see cref="ScreenPalette"/>, so shared pieces follow the screen they're on.
    /// </summary>
    protected YuukiColorPalette Palette { get; private set; }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        // The base loads before the screen itself, so this is cached before anything on the screen loads.
        Palette = colorProvider?.GetPalette(ScreenPart) ?? DefaultYuukiColorProvider.GetDefaultPaletteForPart(ScreenPart);
        Cache(new ScreenPalette(ScreenPart, Palette));

        if (fixedCanvas != null && gameLayout != null)
            BindValueChanged(gameLayout.PhoneWindow, _ => fixedCanvas.TargetDrawSize = gameLayout.GameplayCanvas, true);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (this is IHaveScreenAction haveScreenAction)
        {
            Add(new ScreenActionKeyBindingContainer(haveScreenAction.ScreenActions)
            {
                Child = new ScreenActionContainer()
            });
        }
    }
}

public class ScreenActionKeyBindingContainer : KeyBindingContainer<ScreenAction>
{
    private readonly List<KeyBinding> keyBindings = new List<KeyBinding>();

    public ScreenActionKeyBindingContainer(IEnumerable<ScreenAction> actions)
    {
        foreach (var action in actions)
        {
            if (action.HintOnly)
                continue;

            keyBindings.Add(new KeyBinding(action.KeyCombination, action));
            keyBindings.Add(new KeyBinding(action.GamepadKeyCombination, action));
        }
        SimultaneousMode = SimultaneousBindingMode.All;
        MatchingMode = KeyCombinationMatchingMode.Any;
        SendRepeats = false;
    }

    public override IEnumerable<KeyBinding> DefaultKeyBindings => keyBindings;
}

public class ScreenActionContainer : Component, IKeyBindingHandler<ScreenAction>
{
    public bool OnPressed(KeyBindingPressEvent<ScreenAction> e)
    {
        e.Action.Action.Invoke();
        return true;
    }

    public void OnReleased(KeyBindingReleaseEvent<ScreenAction> e)
    {

    }
}
