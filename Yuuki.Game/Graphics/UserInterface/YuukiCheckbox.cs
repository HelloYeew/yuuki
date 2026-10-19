// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A toggle switch built on top of framework's <see cref="Checkbox"/>
/// </summary>
public partial class YuukiCheckbox : Checkbox
{
    private const float width = 56;
    private const float height = 30;
    private const float knob_inset = 4;
    private const double duration = 200;

    private Box track = null!;
    private Circle knob = null!;
    private YuukiColorPalette palette;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    // Off until loaded: the first state comes from the setting, not a click.
    private bool soundsArmed;

    public YuukiCheckbox()
    {
        Size = new Vector2(width, height);
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider, [CanBeNull] ScreenPalette? screenPalette)
    {
        palette = ScreenPalette.Resolve(screenPalette, colorProvider);

        Children = new Drawable[]
        {
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = height / 2,
                Child = track = new Box
                {
                    RelativeSizeAxes = Axes.Both
                }
            },
            knob = new Circle
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = knob_inset,
                Size = new Vector2(height - knob_inset * 2),
                Color = Color.White
            }
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        soundsArmed = true;
    }

    protected override void OnCheckChanged(bool isChecked)
    {
        if (soundsArmed)
            uiSounds?.Play(isChecked ? UISound.ToggleOn : UISound.ToggleOff);

        track.FadeToColor(isChecked ? palette.Control : palette.Text.WithAlpha(0.3f), duration, Easing.OutQuint);
        knob.MoveToX(isChecked ? width - height + knob_inset : knob_inset, duration, UserInterfaceConstant.SoftBounce);
    }

    protected override void OnHovered()
    {
        knob.ScaleTo(1.1f, 150, Easing.OutQuint);
        uiSounds?.Play(UISound.Hover);
    }

    protected override void OnHoverLost()
    {
        knob.ScaleTo(1f, 150, Easing.OutQuint);
        uiSounds?.Play(UISound.HoverLost);
    }

    protected override void OnFocusGained() => knob.ScaleTo(1.1f, 150, Easing.OutQuint);

    protected override void OnFocusLost() => knob.ScaleTo(1f, 150, Easing.OutQuint);

    protected override void OnEnabledChanged(bool enabled) => this.FadeTo(enabled ? 1f : 0.5f, duration, Easing.OutQuint);
}
