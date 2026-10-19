// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Transforms;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Containers;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// Shared base for the sheared UI buttons (back, next, main-menu).
/// </summary>
public abstract partial class ShearButton : ClickableContainer
{
    /// <summary>
    /// The box drawn behind the button's content. Subclasses assign this while building their layout.
    /// </summary>
    protected Box BackgroundBox = null!;

    /// <summary>
    /// The button's base color. Subclasses read this when building their content.
    /// </summary>
    public Color BackgroundColor { get; set; } = Color.Pink;

    /// <summary>
    /// When true, the button pulses in time with the beatmap's beat.
    /// </summary>
    public bool FlashToBeat { get; set; } = true;

    /// <summary>
    /// When true, the beat pulse only fires while the button is hovered.
    /// Set to false to make the button pulse continuously regardless of hover.
    /// </summary>
    public bool FlashToBeatOnlyWhenHovered { get; set; } = true;

    /// <summary>
    /// How much the background is lightened at the peak of each beat pulse.
    /// </summary>
    protected virtual float BeatFlashAmount => 0.9f;

    /// <summary>
    /// How much the background is lightened while the button is hovered. Kept here so the beat pulse
    /// knows the color to resolve back to (subclasses tint to the same amount on hover).
    /// </summary>
    protected virtual float HoverLightenAmount => 0.5f;

    /// <summary>
    /// The color the background settles to between pulses: the hover tint while hovered, else the base.
    /// The beat pulse always resolves to this, so it can never get stuck on a mid-flash color.
    /// </summary>
    private Color restingColor => IsHovered ? BackgroundColor.Lighten(HoverLightenAmount) : BackgroundColor;

    private BeatSyncContainer beatSync = null!;

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // Invisible, zero-sized helper that ticks off the beatmap BPM, or a steady default beat while
        // nothing plays. AlwaysPresent keeps its Update running even though it draws nothing.
        Add(beatSync = new BeatSyncContainer
        {
            AlwaysPresent = true,
            AllowBeatSyncWhenStopped = true,
            UseDefaultTempoWhenStopped = true
        });
        beatSync.NewBeat += onNewBeat;

        // Hover, un-hover and press sounds; a locked button only says "no".
        Add(new HoverClickSounds(ClickSound) { Enabled = () => !IsLocked });
    }

    /// <summary>
    /// The sound a press makes.
    /// </summary>
    protected virtual UISound ClickSound => UISound.Click;

    /// <summary>
    /// Whether the button is shown but unusable: switched off (<see cref="ClickableContainer.Enabled"/>,
    /// as song select's Next is with nothing listed), or locked by a subclass. It then doesn't flash or
    /// keep the beat, and plays "denied" when pressed.
    /// </summary>
    protected virtual bool IsLocked => !Enabled.Value;

    private void onNewBeat(int beat, double beatLength, ChannelAmplitudes amplitudes)
    {
        // Fade back over most of the beat so each pulse resolves before the next lands.
        if (!FlashToBeat || IsLocked)
            return;

        if (FlashToBeatOnlyWhenHovered && !IsHovered)
            return;

        pulseTo(BackgroundColor.Lighten(BeatFlashAmount), beatLength * 0.9, Easing.OutQuint);
    }

    /// <summary>
    /// Flashes the background to <paramref name="color"/> and back. Nothing while <see cref="IsLocked"/>.
    /// </summary>
    public void Flash(Color color, float duration = 300, Easing easing = Easing.OutQuint)
    {
        if (IsLocked)
            return;

        pulseTo(color, duration, easing);
    }

    /// <summary>
    /// Pops the background to <paramref name="peak"/> and eases it back to <see cref="restingColor"/>.
    /// Uses an explicit clear + set + <c>FadeToColor</c> rather than <c>FlashColor</c>: FlashColor
    /// captures its return color from the drawable's current (possibly mid-flash) color and stacks when
    /// pulses overlap, so a beat landing during another pulse — e.g. right after a BPM change — leaves the
    /// color drifted and stuck. This always resolves to the known resting color instead.
    /// </summary>
    private void pulseTo(Color peak, double duration, Easing easing)
    {
        BackgroundBox.ClearTransforms();
        BackgroundBox.Color = peak;
        BackgroundBox.FadeToColor(restingColor, duration, easing);
    }
}
