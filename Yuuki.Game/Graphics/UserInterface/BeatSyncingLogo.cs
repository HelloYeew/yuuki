// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Containers;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// The yuuki logo that gently pops in time with the beatmap's beat
/// </summary>
public partial class BeatSyncingLogo : BeatSyncContainer
{
    private Sprite sprite = null!;

    /// <summary>
    /// The logo texture to display.
    /// </summary>
    public Texture? Texture { get; set; }

    /// <summary>
    /// The extra scale applied at the peak of each beat (0.04 = +4%).
    /// </summary>
    public float BeatScale { get; set; } = 0.04f;

    [BackgroundDependencyLoader]
    private void load()
    {
        Child = sprite = new Sprite
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Texture = Texture
        };
    }

    protected override void OnNewBeat(int beat, double beatLength, TimeSignature timeSignature, ChannelAmplitudes amplitudes)
    {
        base.OnNewBeat(beat, beatLength, timeSignature, amplitudes);

        // Pop on the beat, punching a little harder when the track is louder, then settle back to
        // rest over most of the beat.
        float amount = BeatScale * (0.6f + 0.4f * amplitudes.Maximum);
        sprite.Scale = new Vector2(1 + amount);
        sprite.ScaleTo(1f, beatLength * 0.9, Easing.OutQuint);
    }
}
