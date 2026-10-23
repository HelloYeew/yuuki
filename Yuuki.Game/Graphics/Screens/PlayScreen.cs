// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Allocation;
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
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Graphics.Screens;

public partial class PlayScreen : YuukiScreen, IHaveScreenAction
{
    /// <summary>
    /// The least room kept either side of the buttons; on a narrower canvas they shrink to keep it.
    /// </summary>
    private const float menu_side_margin = 24;

    private Box menuBox = null!;
    private FlowContainer buttonFlow = null!;

    private Container startBanner = null!;
    private MenuShearButton soloButton = null!;
    private MenuShearButton multiButton = null!;
    private MenuShearButton versusButton = null!;
    private LeftBottomShearButton backButton = null!;

    [Resolved]
    private YuukiScreenStack screenStack { get; set; } = null!;

    public PlayScreen()
    {
        ScreenActions = new List<ScreenAction>()
        {
            new ScreenAction()
            {
                Name = "Solo",
                Action = onSoloButtonClick,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.P))
            },
            new ScreenAction()
            {
                Name = "Multi",
                Action = onMultiButtonClick,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.M))
            },
            new ScreenAction()
            {
                Name = "Versus",
                Action = onVersusButtonClick,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.V))
            },
            new ScreenAction()
            {
                Name = "Back",
                Action = onBackButtonClick,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.Escape))
            }
        };
    }

    public override YuukiScreenPart ScreenPart => YuukiScreenPart.Play;

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        ScreenName.Value = "Play";
        ScreenHelperText.Value = "Goes with the flow";

        Add(startBanner = new Container()
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(230, 100),
            Position = new Vector2(-200, 48),
            Alpha = 0,
            Children = new Drawable[]
            {
                new Container()
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    Shear = UserInterfaceConstant.Shear,
                    CornerRadius = 5,
                    // Its slanted end goes past the screen's edge, under a notch or camera cutout too.
                    Bleed = Edges.Left,
                    Child = new Box()
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both,
                        Color = Palette.Surface
                    }
                },
                new SpriteText()
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "Play",
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 56, FontWeight.Bold),
                    Color = Palette.Text
                }
            }
        });

        Add(menuBox = new Box()
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            RelativeSizeAxes = Axes.X,
            Position = new Vector2(2000, -98),
            Size = new Vector2(1, 150),
            Alpha = 0,
            // Edge to edge, under a notch or a camera cutout too.
            Bleed = Edges.Horizontal,
            Color = ColorExtensions.FromHex("#DFCFBC")
        });

        Add(buttonFlow = new FlowContainer()
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            AutoSizeAxes = Axes.Both,
            Position = new Vector2(2000, -98),
            Spacing = new Vector2(8, 0),
            Alpha = 0,
            Children = new Drawable[]
            {
                soloButton = new MenuShearButton()
                {
                    Icon = IconUsage.Person,
                    Text = "Solo",
                    BackgroundColor = Palette.Accent,
                    Action = onSoloButtonClick
                },
                multiButton = new MenuShearButton()
                {
                    Icon = IconUsage.People,
                    Text = "Multi",
                    BackgroundColor = Palette.TextLight,
                    Action = onMultiButtonClick
                },
                versusButton = new MenuShearButton()
                {
                    Icon = IconUsage.Swords,
                    Text = "Versus",
                    BackgroundColor = Palette.TextMuted,
                    Action = onVersusButtonClick
                }
            }
        });

        Add(backButton = new LeftBottomShearButton()
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Position = new Vector2(-200, LeftBottomShearButton.DEFAULT_POSITION.Y),
            Alpha = 0,
            Action = onBackButtonClick,
            BackgroundColor = Palette.Surface,
        });
    }

    public override void Update()
    {
        base.Update();

        // On a narrow canvas (a phone's, or a big UI scale) the buttons shrink to fit, the bar with them.
        float menuScale = MenuShearButton.ScaleToFit(buttonFlow.DrawSize, DrawWidth - 2 * menu_side_margin);
        buttonFlow.Scale = new Vector2(menuScale);
        menuBox.Height = buttonFlow.DrawHeight * menuScale;
    }

    public override void OnEntering(Screen? last)
    {
        base.OnEntering(last);
        playShowAnimation();
    }

    public override void OnResuming(Screen? last)
    {
        base.OnResuming(last);
        playShowAnimation();
    }

    public override void OnSuspending(Screen next)
    {
        base.OnSuspending(next);
        startBanner.MoveToX(-200, 500, Easing.OutQuint).FadeOut(500, Easing.OutQuint);
        menuBox.MoveToX(-2000, 500, Easing.OutQuint).FadeOut(500, Easing.OutQuint);
        buttonFlow.MoveToX(-2000, 500, Easing.OutQuint).FadeOut(500, Easing.OutQuint);
        backButton.MoveTo(new Vector2(-200, LeftBottomShearButton.DEFAULT_POSITION.Y), 500, Easing.OutQuint)
            .FadeOut(500, Easing.OutQuint);
        this.FadeOut(500, Easing.OutQuint);
    }

    public override void OnExiting(Screen? next)
    {
        base.OnExiting(next);
        playHideAnimation();
    }

    public IReadOnlyList<ScreenAction> ScreenActions { get; }

    private void playHideAnimation()
    {
        startBanner.MoveToX(-200, 500, Easing.OutQuint).FadeOut(500, Easing.OutQuint);
        menuBox.MoveToX(2000, 500, Easing.OutQuint).FadeOut(500, Easing.OutQuint);
        buttonFlow.MoveToX(2000, 500, Easing.OutQuint).FadeOut(500, Easing.OutQuint);
        backButton.MoveTo(new Vector2(-200, LeftBottomShearButton.DEFAULT_POSITION.Y), 500, Easing.OutQuint)
            .FadeOut(500, Easing.OutQuint);
        this.FadeOut(500, Easing.OutQuint);
    }

    private void playShowAnimation()
    {
        startBanner.MoveToX(-40, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        menuBox.MoveToX(0, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        buttonFlow.MoveToX(0, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        backButton.MoveTo(LeftBottomShearButton.DEFAULT_POSITION, 500, Easing.OutQuint)
            .FadeIn(500, Easing.OutQuint);
        this.FadeIn(500, Easing.OutQuint);
    }

    private void onSoloButtonClick()
    {
        soloButton.Flash(Color.White);
        screenStack.Push(new SongSelectionScreen()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        });
    }

    private void onMultiButtonClick()
    {
        multiButton.Flash(Color.White);
    }

    private void onVersusButtonClick()
    {
        versusButton.Flash(Color.White);
    }

    private void onBackButtonClick()
    {
        Exit();
    }
}
