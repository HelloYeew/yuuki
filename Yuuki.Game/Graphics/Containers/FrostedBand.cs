// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.Video;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Graphics.Containers;

/// <summary>
/// The frosted glass song select's lists sit on: the screen palette's surface at half strength over a
/// heavily blurred copy of the background behind it.
/// </summary>
/// <remarks>
/// <para>
/// Sakura has no backdrop blur (a <see cref="BufferedContainer"/> blurs only its own children), so the band
/// holds its own copy of the song's background, lined up with the screen's every frame. It is blurred at a
/// quarter of the resolution, the cheap way to a heavy blur, and cached while nothing moves, so a still
/// background is blurred once per song. The copy skips the screen's dim, which lifts the glass enough for
/// dark text to stay readable over a dark background.
/// </para>
/// <para>
/// When the screen plays the song's video, the copy plays it too: a <see cref="VideoMirror"/> of the
/// screen's own video over the still, faded as the screen fades it, so the glass takes on the colours of
/// whatever moves behind it. The mirror draws the frames the screen has already decoded, and the blur
/// re-runs once per new video frame, not once per display frame.
/// </para>
/// <para>
/// When the player turns "Blur background" off (a low-end choice), the band drops its blur, and with it the
/// video copy, and becomes a plainer, more opaque surface.
/// </para>
/// </remarks>
public partial class FrostedBand : Container
{
    private const float frosted_tint = 0.5f;
    private const float plain_tint = 0.75f;
    private const float blur_sigma = 22f;

    private static readonly Vector2 copy_decode_size = new Vector2(480, 270);

    [Resolved(canBeNull: true)]
    private YuukiBackground? screenBackground { get; set; }

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private Storage? storage { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private ScreenPalette? screenPalette { get; set; }

    [Resolved(canBeNull: true)]
    private IYuukiColorProvider? colorProvider { get; set; }

    private readonly BufferedContainer frost;
    private readonly DelayedLoadCover copy;
    private readonly Container videoCopy;
    private readonly VideoMirror video;
    private readonly Box tint;
    private readonly Box rim;

    private Reactive<bool>? blurBackground;

    /// <summary>
    /// The edge facing the screen, which gets a thin light rim where the glass ends. None for no rim.
    /// </summary>
    public Edges RimEdge
    {
        get => rimEdge;
        set
        {
            rimEdge = value;
            updateRim();
        }
    }

    private Edges rimEdge = Edges.Left;

    public FrostedBand()
    {
        Children = new Drawable[]
        {
            frost = new BufferedContainer
            {
                RelativeSizeAxes = Axes.Both,
                FrameBufferScale = new Vector2(0.25f),
                BlurSigma = new Vector2(blur_sigma),
                CacheDrawnFrameBuffer = true,
                Children = new Drawable[]
                {
                    // Both sized to the screen's background every frame, not to the band.
                    copy = new DelayedLoadCover
                    {
                        RelativeSizeAxes = Axes.None,
                        DecodeTargetSize = copy_decode_size,
                        PlaceholderColor = Color.Black
                    },
                    videoCopy = new Container
                    {
                        RelativeSizeAxes = Axes.None,
                        Alpha = 0,
                        Child = video = new VideoMirror
                        {
                            // Laid out as the screen lays out its video.
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Both,
                            FillMode = TextureFillMode.Fill
                        }
                    }
                }
            },
            tint = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = frosted_tint
            },
            rim = new Box
            {
                Color = Color.White.WithAlpha(0.4f)
            }
        };

        updateRim();
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        tint.Color = ScreenPalette.Resolve(screenPalette, colorProvider).Surface.Lighten(0.2f);

        if (config != null)
        {
            blurBackground = config.Get<bool>(YuukiSetting.BlurBackground);
            BindValueChanged(blurBackground, e => setFrosted(e.NewValue), true);
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // Drawable.BindValueChanged, not the reactive's own: the working beatmap outlives this band.
        // DelayedLoadCover.SetSource also needs the cover loaded first, which it is by now.
        if (workingBeatmap != null)
            BindValueChanged(workingBeatmap.ReactiveWorkingBeatmapSet, e => showSet(e.NewValue), true);
    }

    private void showSet(BeatmapSet? set)
    {
        // A bundled song's background is resolved by the screen background when the song changes, so
        // let that happen first (it runs in the same change notification).
        Scheduler.Add(() => copy.SetSource(set == null ? null : new BeatmapSetBackgroundSource(set, storage)));
    }

    private void setFrosted(bool frosted)
    {
        frost.FadeTo(frosted ? 1 : 0, 300, Easing.OutQuint);
        tint.FadeTo(frosted ? frosted_tint : plain_tint, 300, Easing.OutQuint);
    }

    private void updateRim()
    {
        // A card draws its own outline around all four sides.
        rim.Alpha = rimEdge == Edges.None ? 0 : 1;

        bool side = rimEdge is Edges.Left or Edges.Right;

        rim.RelativeSizeAxes = side ? Axes.Y : Axes.X;
        rim.Size = side ? new Vector2(1, 1) : new Vector2(1, 1);
        rim.Anchor = rim.Origin = rimEdge switch
        {
            Edges.Left => Anchor.CentreLeft,
            Edges.Right => Anchor.CentreRight,
            Edges.Bottom => Anchor.BottomCentre,
            _ => Anchor.TopCentre
        };
    }

    public override void Update()
    {
        base.Update();

        if (screenBackground == null || frost.Alpha <= 0)
        {
            // Nothing to draw it into, so it shouldn't hold any of the screen's video frames either.
            video.Source = null;
            return;
        }

        // Line the copies up with the screen's background, wherever the band is (it slides in and out).
        Quad background = screenBackground.ScreenSpaceDrawQuad;
        var topLeft = frost.ToLocalSpace(background.TopLeft);
        var bottomRight = frost.ToLocalSpace(background.BottomRight);

        copy.Position = videoCopy.Position = topLeft;
        copy.Size = videoCopy.Size = bottomRight - topLeft;

        video.Source = screenBackground.Video;
        videoCopy.Alpha = screenBackground.VideoOpacity;
    }
}
