// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Configurations;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// Gives the <see cref="Playfield"/> the same shape on every window.
/// </summary>
public partial class PlayfieldAdjustmentContainer : Container
{
    /// <summary>
    /// The canvas the playfield is laid out on, in its own units: the centre 72% of the 1920×1080
    /// reference, full height. On a 16:9 window at full size it fills the height exactly.
    /// </summary>
    public static readonly Vector2 BASE_SIZE = new Vector2(1920 * 0.72f, 1080);

    private readonly Container content;

    protected override Container Content => content;

    /// <summary>
    /// How big the field is, as a fraction of the largest that fits
    /// (<see cref="YuukiConfigManager.MIN_PLAYFIELD_SIZE"/>–<see cref="YuukiConfigManager.MAX_PLAYFIELD_SIZE"/>).
    /// </summary>
    public float PlayfieldSize { get; set; } = 1;

    /// <summary>
    /// Where the field sits in the room it leaves, per axis, 0–1: 0 against the left/top edge, 1
    /// against the right/bottom, 0.5 centred.
    /// </summary>
    public Vector2 PlayfieldPosition { get; set; } = new Vector2(0.5f);

    /// <summary>
    /// The scale the <see cref="BASE_SIZE"/> canvas is currently drawn at (0 before the first layout).
    /// </summary>
    public float CurrentScale { get; private set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    public PlayfieldAdjustmentContainer()
    {
        RelativeSizeAxes = Axes.Both;

        AddInternal(content = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = BASE_SIZE
        });
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        if (config == null)
            return;

        BindValueChanged(config.Get<double>(YuukiSetting.PlayfieldSize), e => PlayfieldSize = (float)e.NewValue, true);
        BindValueChanged(config.Get<double>(YuukiSetting.PlayfieldPositionX), e => PlayfieldPosition = new Vector2((float)e.NewValue, PlayfieldPosition.Y), true);
        BindValueChanged(config.Get<double>(YuukiSetting.PlayfieldPositionY), e => PlayfieldPosition = new Vector2(PlayfieldPosition.X, (float)e.NewValue), true);
    }

    public override void Update()
    {
        base.Update();

        Vector2 available = DrawSize;
        if (available.X <= 0 || available.Y <= 0)
            return;

        float size = Math.Clamp(PlayfieldSize, (float)YuukiConfigManager.MIN_PLAYFIELD_SIZE, (float)YuukiConfigManager.MAX_PLAYFIELD_SIZE);
        float scale = Math.Min(available.X / BASE_SIZE.X, available.Y / BASE_SIZE.Y) * size;

        // Positioned within the room left around the scaled field, never past the window's edges.
        Vector2 room = new Vector2(available.X - BASE_SIZE.X * scale, available.Y - BASE_SIZE.Y * scale);
        Vector2 offset = new Vector2(
            room.X * (Math.Clamp(PlayfieldPosition.X, 0, 1) - 0.5f),
            room.Y * (Math.Clamp(PlayfieldPosition.Y, 0, 1) - 0.5f));

        if (scale == CurrentScale && content.Position == offset)
            return;

        CurrentScale = scale;
        content.Scale = new Vector2(scale);
        content.Position = offset;
    }
}
