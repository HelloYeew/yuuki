// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Screens;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Graphics.Screens;

public partial class StartScreen : YuukiScreen
{
    private Sprite frameworkLogo = null!;
    private BeatSyncingLogo logo = null!;
    private SpriteText startText = null!;

    public override bool HideFooter => true;

    public override bool HideVideo => false;

    public override YuukiScreenPart ScreenPart => YuukiScreenPart.MainMenu;

    [Resolved(canBeNull: true)]
    private YuukiScreenStack? screenStack;

    [Resolved(canBeNull: true)]
    private YuukiGame? game;

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout;

    [BackgroundDependencyLoader]
    private void load(ITextureManager textureManager)
    {
        ScreenName.Value = "Start Screen";

        Add(frameworkLogo = new Sprite()
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Size = new Vector2(280, 50),
            Position = new Vector2(-350, -48),
            Texture = textureManager.Get("framework-logo.png")
        });

        Add(logo = new BeatSyncingLogo()
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(340, 138),
            Position = new Vector2(-450, 48),
            Texture = textureManager.Get("logo.png")
        });

        Add(startText = new SpriteText()
        {
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomRight,
            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 32, FontWeight.Bold),
            Position = new Vector2(400, -48)
        });

        setPrompt(phone: false);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // The layout is picked from the window once the game is running, which can be after this loads.
        if (gameLayout != null)
            BindValueChanged(gameLayout.Current, e => setPrompt(e.NewValue == DisplayLayout.Phone), true);
    }

    /// <summary>
    /// A phone has no keys to press.
    /// </summary>
    private void setPrompt(bool phone)
    {
        string prompt = phone ? "Tap anywhere to start" : "Press any key to start";
        ScreenHelperText.Value = prompt;
        startText.Text = prompt;
    }

    public override void OnEntering(Screen? last)
    {
        base.OnEntering(last);
        frameworkLogo.MoveToX(48, 800, Easing.OutQuint);
        logo.MoveToX(48, 800, Easing.OutQuint);
        startText.MoveToX(-48, 800, Easing.OutQuint);
        Scheduler.AddDelayed(() => startText.FadeTo(0.25f).FadeTo(1, 1000).Loop(), 800);
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        pushMainMenuScreen();
        return base.OnMouseDown(e);
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        pushMainMenuScreen();
        return base.OnKeyDown(e);
    }

    public override bool OnGamepadButtonDown(GamepadButtonEvent e)
    {
        pushMainMenuScreen();
        return base.OnGamepadButtonDown(e);
    }

    public override void OnSuspending(Screen next)
    {
        base.OnSuspending(next);
        this.FadeOut(500, Easing.OutQuint);
    }

    public override void OnExiting(Screen? next)
    {
        base.OnSuspending(next);
        this.FadeOut(500, Easing.OutQuint);
    }

    private void pushMainMenuScreen()
    {
        screenStack?.Push(new MainMenuScreen()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        });
        game?.ShowFooter.Value = true;
    }
}
