// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Reactive;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Graphics.Drawables;

/// <summary>
/// Clock footer
/// </summary>
public partial class FooterClock : Container
{
    private const float padding = 10;
    private const float spacing = 5;

    private readonly Box background;
    private readonly Box flash;
    private readonly AnalogClockDisplay analog;
    private readonly DigitalClockDisplay digital;

    private Reactive<ClockDisplayMode> displayMode = new Reactive<ClockDisplayMode>(ClockDisplayMode.Full);
    private Reactive<bool> prefer24HourTime = new Reactive<bool>(true);

    public FooterClock()
    {
        Height = 38;
        AutoSizeAxes = Axes.X;
        Masking = true;
        CornerRadius = 8;

        Children = new Drawable[]
        {
            background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = Color.White,
                Alpha = 0.15f
            },
            flash = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = Color.White,
                Alpha = 0,
                Blending = BlendingMode.Additive
            },
            new Container
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                AutoSizeAxes = Axes.Both,
                Margin = new MarginPadding { Left = padding, Right = padding },
                Children = new Drawable[]
                {
                    analog = new AnalogClockDisplay
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft
                    },
                    digital = new DigitalClockDisplay
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft
                    }
                }
            },
            new HoverClickSounds()
        };

        // Black until the footer's palette arrives, as its icons are.
        applyColors(Color.Black, Color.Black, Color.White, 0);
    }

    /// <summary>
    /// The mode showing now. Clicking steps it on.
    /// </summary>
    public ClockDisplayMode DisplayMode => displayMode.Value;

    public bool ShowsFace => analog.Alpha > 0;

    public bool ShowsDigits => digital.Alpha > 0;

    public bool ShowsRuntime => ShowsDigits && digital.ShowRuntime;

    /// <summary>
    /// The time as the digits read it, e.g. "15:04:05" or "3:04:05 PM".
    /// </summary>
    public string ShownTime => digital.ShownTime;

    /// <summary>
    /// How long the game has been running, as shown, e.g. "00:12:34".
    /// </summary>
    public string ShownRunningTime => digital.ShownRunningTime;

    [BackgroundDependencyLoader]
    private void load([CanBeNull] YuukiConfigManager? configManager)
    {
        if (configManager.IsNotNull())
        {
            displayMode = configManager!.Get<ClockDisplayMode>(YuukiSetting.ClockDisplayMode);
            prefer24HourTime = configManager.Get<bool>(YuukiSetting.Prefer24HourTime);
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        BindValueChanged(displayMode, e =>
        {
            bool showAnalog = e.NewValue == ClockDisplayMode.Analog || e.NewValue == ClockDisplayMode.Full;
            bool showDigital = e.NewValue != ClockDisplayMode.Analog;
            bool showRuntime = e.NewValue == ClockDisplayMode.DigitalWithRuntime || e.NewValue == ClockDisplayMode.Full;

            // Hidden ones drop out of the autosize, so the button fits what's showing.
            analog.Alpha = showAnalog ? 1 : 0;
            digital.Alpha = showDigital ? 1 : 0;
            digital.ShowRuntime = showRuntime;
            digital.X = showAnalog ? analog.Width + spacing : 0;
        }, true);

        BindValueChanged(prefer24HourTime, e => digital.Use24HourDisplay = e.NewValue, true);
    }

    public void SetAccent(Color accent, Color highlight, Color outline) => applyColors(accent, highlight, outline, 250);

    private void applyColors(Color accent, Color highlight, Color outline, double duration)
    {
        analog.SetColors(accent, highlight, outline, duration);
        digital.SetColors(accent, highlight, duration);
    }

    public override bool OnHover(MouseEvent e)
    {
        background.FadeTo(0.35f, 150, Easing.OutQuint);
        return true;
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        background.FadeTo(0.15f, 150, Easing.OutQuint);
        return true;
    }

    public override bool OnClick(MouseButtonEvent e)
    {
        flash.ClearTransforms();
        flash.Alpha = 0.4f;
        flash.FadeOut(800, Easing.OutQuint);

        cycleDisplayMode();
        return true;
    }

    private void cycleDisplayMode()
    {
        displayMode.Value = displayMode.Value switch
        {
            ClockDisplayMode.Analog => ClockDisplayMode.Full,
            ClockDisplayMode.Digital => ClockDisplayMode.Analog,
            ClockDisplayMode.DigitalWithRuntime => ClockDisplayMode.Digital,
            _ => ClockDisplayMode.DigitalWithRuntime
        };
    }
}
