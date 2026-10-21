// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class DelayedLoadCover : Container
{
    /// <summary>
    /// Duration (ms) of the fade applied when a freshly loaded cover appears.
    /// </summary>
    public double FadeDuration { get; set; } = 400;

    public Easing FadeEasing { get; set; } = Easing.OutQuint;

    /// <summary>
    /// The scale a freshly loaded cover starts at before easing to 1 as it fades in, giving a gentle
    /// zoom-settle instead of a flat fade. 1 disables it (the default — e.g. for a full-screen
    /// background that shouldn't move).
    /// </summary>
    public float LoadInScale { get; set; } = 1f;

    /// <summary>
    /// How the cover texture fills the component's bounds.
    /// </summary>
    public TextureFillMode CoverFillMode { get; set; } = TextureFillMode.Fill;

    /// <summary>
    /// Approximate on-screen size (px) this cover will be displayed at. When set, the source image is
    /// downscaled (and, under <see cref="TextureFillMode.Fill"/>, centre-cropped to the display aspect)
    /// during decode so we never decode or upload far more pixels than can be shown. <c>null</c> keeps
    /// the full-resolution image — the right choice for a full-screen background, the wrong one for a
    /// small card. Set before <see cref="SetSource"/> takes effect.
    /// </summary>
    public Vector2? DecodeTargetSize { get; set; }

    /// <summary>
    /// The color shown behind/instead of the cover until (and unless) an image is loaded.
    /// </summary>
    public Color PlaceholderColor
    {
        get => placeholderColor;
        set
        {
            placeholderColor = value;
            if (placeholder != null)
                placeholder.Color = value;
        }
    }

    private Color placeholderColor = Color.Transparent;

    /// <summary>
    /// Raised (on the update thread) whenever a cover finishes loading and becomes visible.
    /// </summary>
    public event Action<Texture>? CoverLoaded;

    /// <summary>
    /// Whether a cover image is currently displayed (as opposed to just the placeholder).
    /// </summary>
    public bool HasCover { get; private set; }

    [Resolved(canBeNull: true)]
    private ITextureManager? textureManager { get; set; }

    [Resolved(canBeNull: true)]
    private CoverColorStore? coverColors { get; set; }

    private readonly Box placeholder;
    private readonly Container coverLayer;

    private int generation;
    private CancellationTokenSource? cancellation;

    public DelayedLoadCover()
    {
        RelativeSizeAxes = Axes.Both;

        Add(placeholder = new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Color = placeholderColor
        });
        Add(coverLayer = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        });
    }

    /// <summary>
    /// Point the cover at a new source (or <c>null</c> to clear back to the placeholder). Any
    /// in-flight load for a previous source is cancelled.
    /// </summary>
    public void SetSource(ICoverSource? source)
    {
        int gen = ++generation;

        cancellation?.Cancel();
        cancellation = null;

        // Clearing disposes whatever was on display, and a CoverLoader releases its own texture on
        // disposal — so this is also what frees the cover we were showing.
        coverLayer.Clear();
        HasCover = false;

        placeholder.ClearTransforms();
        placeholder.Alpha = 1;

        if (source == null)
            return;

        // Already decoded elsewhere (e.g. an atlas-cached embedded texture): show it right away. We
        // don't own it, so it's never disposed here.
        var cached = source.Cached;
        if (cached != null)
        {
            display(createSprite(cached), animateIn: false);
            CoverLoaded?.Invoke(cached);
            return;
        }

        // No renderer available (e.g. a headless context with no texture manager). Decoding now lives
        // behind the texture manager, so this no longer needs an IImageLoader of its own.
        if (textureManager == null)
            return;

        // Deliberately not disposed: LoadComponentAsync hands this token to a background task and a
        // continuation, so the token can still be read after we cancel. A CancellationTokenSource with no
        // timer and no allocated WaitHandle is a small managed object with nothing unmanaged attached, so
        // letting it be collected is cheaper than risking an ObjectDisposedException in the TPL.
        cancellation = new CancellationTokenSource();

        var loader = new CoverLoader(source, textureManager, CoverFillMode, DecodeTargetSize, coverColors);

        // No discard handler: the framework disposes a load it had to throw away, and CoverLoader releases
        // its texture on disposal. That covers cancellation, a load that faults part-built, and this cover
        // being torn down while the load is still in flight — the last of which used to leak permanently,
        // because the release only ever ran from the onLoaded callback that cancellation skips.
        LoadComponentAsync(loader, loaded =>
        {
            // Source changed (or the card was recycled) between the load finishing and this callback
            // running. Dispose rather than display: the loader owns its texture, so this releases it.
            if (gen != generation || loaded.Texture == null)
            {
                loaded.Dispose();
                return;
            }

            display(loaded, animateIn: true);
            CoverLoaded?.Invoke(loaded.Texture);
        }, cancellation.Token);
    }

    /// <summary>
    /// Cancels any in-flight load. The displayed cover's texture is released by the cascade reaching the
    /// <see cref="CoverLoader"/> holding it, and a load still in flight is released by the framework's
    /// discard path — so there is nothing texture-shaped left to do here.
    /// </summary>
    protected override void Dispose(bool isDisposing)
    {
        cancellation?.Cancel();
        cancellation = null;

        base.Dispose(isDisposing);
    }

    private void display(Sprite sprite, bool animateIn)
    {
        coverLayer.Clear();
        coverLayer.Add(sprite);
        HasCover = true;

        if (animateIn)
        {
            sprite.FadeInFromZero(FadeDuration, FadeEasing);

            // Subtle zoom-settle so the image eases into focus rather than flatly fading.
            if (Math.Abs(LoadInScale - 1f) > 0.001f)
            {
                sprite.Scale = new Vector2(LoadInScale);
                sprite.ScaleTo(1f, FadeDuration, FadeEasing);
            }

            // Cross-fade the placeholder out a touch slower so there's never a bare gap behind the cover.
            placeholder.FadeOut(FadeDuration * 1.1, FadeEasing);
        }
        else
        {
            sprite.Alpha = 1;
            placeholder.Alpha = 0;
        }
    }

    private Sprite createSprite(Texture texture) => new Sprite
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre,
        RelativeSizeAxes = Axes.Both,
        FillMode = CoverFillMode,
        Texture = texture
    };

    /// <summary>
    /// Decodes a cover on a background load thread and displays it, owning the resulting texture for as
    /// long as it is on screen.
    /// </summary>
    /// <remarks>
    /// Ownership sits here rather than on <see cref="DelayedLoadCover"/> because this is the drawable the
    /// texture is attached to: while it is displayed the texture is in use, and when it stops being
    /// displayed — a new source replacing it, the card being recycled, the screen being torn down, or the
    /// load being discarded before it was ever shown — the same disposal releases it. One owner, one
    /// release, and no hook that has to guess which of those happened.
    /// </remarks>
    private partial class CoverLoader : Sprite
    {
        private readonly ICoverSource source;
        private readonly ITextureManager textureManager;
        private readonly TextureFillMode fillMode;
        private readonly Vector2? decodeTargetSize;
        private readonly CoverColorStore? coverColors;

        /// <summary>
        /// The share key this cover's texture was acquired under, or null if it is unshared and owned
        /// outright. Decides which release path <see cref="Dispose"/> takes.
        /// </summary>
        public string? ShareKey { get; private set; }

        [Resolved(canBeNull: true)]
        private AppHost? host { get; set; }

        public CoverLoader(ICoverSource source, ITextureManager textureManager, TextureFillMode fillMode, Vector2? decodeTargetSize, CoverColorStore? coverColors)
        {
            this.source = source;
            this.textureManager = textureManager;
            this.fillMode = fillMode;
            this.decodeTargetSize = decodeTargetSize;
            this.coverColors = coverColors;

            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            RelativeSizeAxes = Axes.Both;
            FillMode = fillMode;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // The share key has to be built before the stream is opened: on a hit the framework returns
            // the existing texture without reading it at all, which is the whole point.
            string? shareKey = source.CacheKey == null
                ? null
                : TextureCreationOptions.ShareKeyFor(source.CacheKey, decodeTargetSize, fillMode);

            using var stream = source.Open();
            if (stream == null)
                return;

            // One call replaces decode + downscale + centre-crop + pooled buffering + upload + naming,
            // all of which this class used to do by hand. Decoding still happens on this background load
            // thread; a card only shows a small region, so decoding a full 4K background would waste CPU,
            // memory and GPU upload bandwidth. Under Fill the framework crops the centre band we actually
            // display before scaling, so the pixels clipped off screen are never decoded or uploaded.
            Texture = textureManager.CreateFromStream(stream, new TextureCreationOptions
            {
                Decode = new ImageLoadOptions(decodeTargetSize, fillMode),

                // Names the card in the framework's texture viewer (Ctrl + F3). Covers are usually the
                // largest textures in the game, and without this they are anonymous same-sized cards.
                Name = source.CacheKey ?? "cover",

                // The same beatmap set's image shown in more than one place at the same size — a carousel
                // card and the music player row, say — becomes one GPU texture instead of one per display.
                // The size and fill mode are part of the key because they change the decoded pixels: a
                // card face and a full-screen background of the same image are genuinely different
                // textures.
                ShareKey = shareKey,

                // The cover's colour, read from the pixels this decode makes anyway. Only on the first
                // decode of a cover: after that the colour is known, and a share hit decodes nothing.
                InspectPixels = colorKeyToPick() is { } colorKey
                    ? (rgba, width, height) => coverColors!.Pick(colorKey, rgba, width, height)
                    : null
            });

            if (Texture != null)
                ShareKey = shareKey;
        }

        private string? colorKeyToPick()
        {
            if (coverColors == null || !source.PickColor || source.CacheKey is not { } key || coverColors.Contains(key))
                return null;

            return key;
        }

        /// <summary>
        /// Gives back this cover's texture, matching how it was acquired.
        /// </summary>
        protected override void Dispose(bool isDisposing)
        {
            var texture = Texture;
            string? shareKey = ShareKey;

            // Cleared before releasing so a second disposal cannot release the same texture twice, and so
            // nothing draws from it in the frames between the schedule below and the release running.
            Texture = null;
            ShareKey = null;

            if (texture != null)
            {
                if (shareKey != null)
                {
                    // A shared texture may still be on screen somewhere else, so it is released by key and
                    // the framework disposes it once the last holder lets go. Disposing it directly here
                    // would blank every other display of the same image.
                    textureManager.ReleaseSharedTexture(shareKey);
                }
                else if (host != null)
                {
                    // Disposal runs on the update thread (it is budgeted through DrawableDisposalQueue), so
                    // the native destroy has to be marshalled: freeing a GPU texture off the draw thread is
                    // invalid on every backend.
                    //
                    // And dispose the Texture, not its backend. Reaching past it frees the GPU memory but
                    // leaves the texture registered and undisposed, so it lingers in the Ctrl+F3 viewer as
                    // a white card and counts toward live VRAM forever. Texture.Dispose also knows not to
                    // touch a texture it does not own (an atlas slice, or a shared fallback).
                    host.Renderer.ScheduleToDrawThread(texture.Dispose);
                }
            }

            base.Dispose(isDisposing);
        }
    }
}
