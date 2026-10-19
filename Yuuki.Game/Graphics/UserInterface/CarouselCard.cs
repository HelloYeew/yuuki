// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Pooling;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;

namespace Yuuki.Game.Graphics.UserInterface;

public abstract partial class CarouselCard<TModel> : PoolableDrawable
{
    /// <summary>
    /// The carousel's orientation reactive, handed to the card by the pool setup action before
    /// <see cref="Apply"/>. The card binds to it on attach so it can reshape itself live when the
    /// carousel is switched between horizontal and vertical.
    /// </summary>
    public Reactive<CarouselOrientation>? Orientation { get; set; }

    /// <summary>
    /// A card's edge in the list: a hollow (rim-only) drop shadow that separates stacked panels,
    /// Hollow keeps the effect on the edge instead of washing the whole card.
    /// </summary>
    protected static readonly EdgeEffectParameters SHADOW_EDGE = new EdgeEffectParameters
    {
        Type = EdgeEffectType.Shadow,
        Color = Color.Black.WithAlpha(0.35f),
        Radius = 4f,
        Offset = new Vector2(0f, 2f),
        Hollow = true
    };

    /// <summary>
    /// The selected card's edge: a white ring of glow in place of the shadow.
    /// </summary>
    protected static readonly EdgeEffectParameters SELECTED_EDGE = new EdgeEffectParameters
    {
        Type = EdgeEffectType.Glow,
        Color = Color.White.WithAlpha(0.5f),
        Radius = 7f,
        Hollow = true
    };

    protected CarouselCard()
    {
        // Quiet: a moving list brushes a lot of cards past the pointer. Selecting plays its own sound.
        Add(new HoverClickSounds(null) { HoverVolume = 0.5 });
    }

    /// <summary>
    /// Reconfigure this (possibly recycled) card for the given model. <paramref name="index"/> is the
    /// model's position in the carousel, handy for e.g. picking a colour from a palette.
    /// </summary>
    public abstract void Apply(TModel model, int index);

    /// <summary>
    /// Called when the owning carousel's orientation changes (and once on attach). Subclasses reshape
    /// their internal layout here — e.g. swapping a square cover-flow face for a wide bar face.
    /// </summary>
    protected virtual void OnOrientationChanged(CarouselOrientation orientation)
    {
    }

    private void onOrientationChanged(ValueChangedEvent<CarouselOrientation> e) => OnOrientationChanged(e.NewValue);

    protected override void OnParentChanged()
    {
        if (Parent == null)
        {
            if (Orientation != null)
                Orientation.ValueChanged -= onOrientationChanged;
        }
        else if (Orientation != null)
        {
            Orientation.ValueChanged += onOrientationChanged;
            OnOrientationChanged(Orientation.Value);
        }

        base.OnParentChanged();
    }
}
