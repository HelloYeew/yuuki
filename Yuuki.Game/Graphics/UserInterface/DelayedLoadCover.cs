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

    /// <summary>
    /// Whether a cover is being decoded to show: the placeholder is up, and the cover will replace it.
    /// </summary>
    public bool IsLoading { get; private set; }

    [Resolved(canBeNull: true)]
    private ITextureManager? textureManager { get; set; }

    [Resolved(canBeNull: true)]
    private CoverColorStore? coverColors { get; set; }

    [Resolved(canBeNull: true)]
    private RecentCoverTextures? recentCovers { get; set; }

    private readonly Box placeholder;
    private readonly Container coverLayer;

    private int generation;
    private CancellationTokenSource? cancellation;

    // The cover on display, when it's one this loaded (rather than a source's cached texture).
    private CoverLoader? shown;

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
    /// <param name="source">The image to show.</param>
    /// <param name="decode">False to show it only if it's at hand (on show elsewhere, or kept from a moment
    /// ago) and otherwise stay on the placeholder, decoding nothing: for a cover that would be gone before
    /// its decode was done. Set the source again to decode it.</param>
    public void SetSource(ICoverSource? source, bool decode = true)
    {
        int gen = ++generation;

        cancellation?.Cancel();
        cancellation = null;
        IsLoading = false;

        var cached = source?.Cached;
        string? shareKey = source?.CacheKey == null || cached != null ? null : shareKeyFor(source.CacheKey);

        // Pointed again at the image it shows (a recycled card given the same song, say): it stays.
        if (shareKey != null && shareKey == shown?.ShareKey)
            return;

        // Taken before what's on display is cleared, which may be letting go of it.
        var held = shareKey != null ? takeHeld(source!, shareKey) : null;

        // Clearing disposes whatever was on display, and a CoverLoader releases its own texture on
        // disposal — so this is also what frees the cover we were showing.
        coverLayer.Clear();
        shown = null;
        HasCover = false;

        placeholder.ClearTransforms();
        placeholder.Alpha = 1;

        if (source == null)
            return;

        // Already decoded elsewhere (e.g. an atlas-cached embedded texture): show it right away. We
        // don't own it, so it's never disposed here.
        if (cached != null)
        {
            display(createSprite(cached), animateIn: false);
            CoverLoaded?.Invoke(cached);
            return;
        }

        // Decoded at this size already: on show somewhere else, or let go of a moment ago and kept (see
        // RecentCoverTextures). Shown at once, without the fade, as it's been on screen just now.
        if (held != null)
        {
            display(held, animateIn: false);
            CoverLoaded?.Invoke(held.Texture!);
            return;
        }

        // No renderer available (e.g. a headless context with no texture manager). Decoding now lives
        // behind the texture manager, so this no longer needs an IImageLoader of its own.
        if (textureManager == null || !decode)
            return;

        // Deliberately not disposed: LoadComponentAsync hands this token to a background task and a
        // continuation, so the token can still be read after we cancel. A CancellationTokenSource with no
        // timer and no allocated WaitHandle is a small managed object with nothing unmanaged attached, so
        // letting it be collected is cheaper than risking an ObjectDisposedException in the TPL.
        cancellation = new CancellationTokenSource();

        var loader = new CoverLoader(source, textureManager, CoverFillMode, DecodeTargetSize, coverColors, recentCovers);
        IsLoading = true;

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
                if (gen == generation)
                    IsLoading = false;

                loaded.Dispose();
                return;
            }

            IsLoading = false;
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
        shown = sprite as CoverLoader;
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

    private string shareKeyFor(string cacheKey) => TextureCreationOptions.ShareKeyFor(cacheKey, DecodeTargetSize, CoverFillMode);

    /// <summary>
    /// The cover for <paramref name="source"/> if its texture is held already at this size, taking a
    /// reference to it: from the covers kept warm (taking theirs over), or from wherever it's on show.
    /// </summary>
    private CoverLoader? takeHeld(ICoverSource source, string shareKey)
    {
        if (textureManager == null)
            return null;

        if (recentCovers?.TryTake(shareKey, textureManager, out var texture) != true
            && !textureManager.TryAcquireSharedTexture(shareKey, out texture))
            return null;

        return new CoverLoader(source, textureManager, CoverFillMode, DecodeTargetSize, coverColors, recentCovers, (texture, shareKey));
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
        private readonly RecentCoverTextures? recentCovers;

        /// <summary>
        /// The share key this cover's texture was acquired under, or null if it is unshared and owned
        /// outright. Decides which release path <see cref="Dispose"/> takes.
        /// </summary>
        public string? ShareKey { get; private set; }

        [Resolved(canBeNull: true)]
        private AppHost? host { get; set; }

        // held: a shared texture already acquired for this cover, and the key it's shared under, so there's
        // nothing to decode.
        public CoverLoader(ICoverSource source, ITextureManager textureManager, TextureFillMode fillMode, Vector2? decodeTargetSize, CoverColorStore? coverColors,
                           RecentCoverTextures? recentCovers, (Texture Texture, string ShareKey)? held = null)
        {
            this.source = source;
            this.textureManager = textureManager;
            this.fillMode = fillMode;
            this.decodeTargetSize = decodeTargetSize;
            this.coverColors = coverColors;
            this.recentCovers = recentCovers;

            if (held is (var texture, var shareKey))
            {
                Texture = texture;
                ShareKey = shareKey;
            }

            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            RelativeSizeAxes = Axes.Both;
            FillMode = fillMode;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // Made with its texture (see takeHeld).
            if (Texture != null)
                return;

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
                    // would blank every other display of the same image. Kept a while first, when the game
                    // keeps recent covers, so showing it again needn't decode it again.
                    if (recentCovers != null)
                        recentCovers.Retire(shareKey, texture, textureManager);
                    else
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
