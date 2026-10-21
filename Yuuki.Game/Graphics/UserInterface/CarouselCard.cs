// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Pooling;
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
    /// How the carousel is moving, handed to the card with <see cref="Orientation"/>: a card that
    /// compacts while the list skims reads <see cref="CarouselMotion.Skim"/> from it.
    /// </summary>
    public CarouselMotion? Motion { get; set; }

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
    /// their internal layout here — e.g. swapping a strip tile for a rail row.
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
