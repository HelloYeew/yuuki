// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.ObjectExtensions;
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
using Sakura.Framework.Utilities;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Graphics.Containers;

public partial class YuukiBackground : Container
{
    private const double fade_duration = 500;

    [Resolved]
    private WorkingBeatmap workingBeatmap { get; set; } = null!;

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private Storage? storage { get; set; }

    [Resolved(canBeNull: true)]
    private ITextureManager? textureManager { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiAudioManager? audioManager { get; set; }

    [Resolved(canBeNull: true)]
    private IWindow? window { get; set; }

    private DelayedLoadCover background = null!;
    private Container videoLayer = null!;
    private Box dimBox = null!;
    private BufferedContainer blurContainer = null!;

    private VideoSprite? currentVideo;
    private Container? currentVideoContainer;

    /// <summary>
    /// Max drift (ms) between video and audio before a corrective seek. Big enough to ignore audio
    /// start-up/buffer jitter (so it doesn't seek repeatedly), small enough that a real desync is caught.
    /// </summary>
    private const double resync_threshold = 250;

    private BeatmapSet? currentSet;

    private bool screenHideVideo = true;

    /// <summary>
    /// Whether the video layer is shown, or fading in; the fade out is only finished once its alpha reaches 0.
    /// </summary>
    private bool videoShown;

    /// <summary>
    /// Whether the video layer is showing (or fading in) — the screen, <see cref="YuukiSetting.DisableVideo"/>
    /// and, on play screens, <see cref="YuukiSetting.DisableGameplayVideo"/> all permitting. Exposed
    /// mainly so tests can assert it.
    /// </summary>
    public bool VideoShown => videoShown;

    private Reactive<bool>? disableVideo;
    private Reactive<bool>? disableGameplayVideo;
    private Reactive<int>? backgroundDim;
    private Reactive<bool>? blurBackground;
    private Reactive<int>? gameplayBlur;

    /// <summary>
    /// Whether the current screen wants the playfield dim applied (gameplay only).
    /// </summary>
    private bool gameplayDimActive;

    private bool blurAllowed;
    private bool blurSuppressed;

    private const float blur_strength = 5f;

    /// <summary>
    /// Gaussian sigma at 100% gameplay blur (<see cref="YuukiSetting.BackgroundBlur"/>)
    /// </summary>
    private const float gameplay_blur_max_strength = 25f;

    private float currentBlurStrength;
    private float targetBlurStrength;

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.Both;

        Add(blurContainer = new BufferedContainer(false)
        {
            RelativeSizeAxes = Axes.Both,
            FrameBufferScale = Vector2.One,
            Child = background = new DelayedLoadCover
            {
                RelativeSizeAxes = Axes.Both,
                PlaceholderColor = Color.Black,
                FadeDuration = fade_duration
            }
        });
        Add(videoLayer = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Alpha = 0
        });
        Add(dimBox = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Color = Color.Black,
            Alpha = 0
        });

        if (config != null)
        {
            disableVideo = config.Get<bool>(YuukiSetting.DisableVideo);
            disableVideo.BindValueChanged(_ => onDisableVideoChanged());

            disableGameplayVideo = config.Get<bool>(YuukiSetting.DisableGameplayVideo);
            disableGameplayVideo.BindValueChanged(_ => updateVideoLayerAlpha());

            backgroundDim = config.Get<int>(YuukiSetting.BackgroundDim);
            backgroundDim.BindValueChanged(_ => updateDim());

            blurBackground = config.Get<bool>(YuukiSetting.BlurBackground);
            blurBackground.BindValueChanged(_ => updateBlur());

            gameplayBlur = config.Get<int>(YuukiSetting.BackgroundBlur);
            gameplayBlur.BindValueChanged(_ => updateBlur());
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        workingBeatmap.ReactiveWorkingBeatmapSet.BindValueChanged(e => onBeatmapSetChanged(e.NewValue), true);
    }

    /// <summary>
    /// Shows <paramref name="set"/>'s background without it being the working set: the editor showing a
    /// background picked in Setup before it's saved. The working set comes back when it next changes.
    /// </summary>
    public void ShowSet(BeatmapSet set) => onBeatmapSetChanged(set);

    private void onBeatmapSetChanged(BeatmapSet set)
    {
        currentSet = set.IsNull() ? null : set;

        if (set.IsNull())
        {
            background.SetSource(null);
            updateVideo(null);
            return;
        }

        var decodeSize = backgroundDecodeSize();

        // A local source's background lives in resources rather than storage, so BeatmapSetBackgroundSource
        // cannot open it and the texture has to be resolved here instead. Decoded at the same size the
        // normal path would use: this texture becomes the source's Cached, which short-circuits
        // DelayedLoadCover entirely, so DecodeTargetSize below never gets to apply to it. Asking Get() for
        // a full-resolution decode here meant the bundled background — the one on screen the longest —
        // was the one image that ignored the window-size cap.
        if (set.UseLocalSource && set.Background.IsNull() && textureManager != null && !string.IsNullOrEmpty(set.BackgroundPath))
            set.Background = textureManager.Get(set.BackgroundPath, new ImageLoadOptions(decodeSize, background.CoverFillMode));

        background.DecodeTargetSize = decodeSize;
        background.SetSource(new BeatmapSetBackgroundSource(set, storage));
        updateVideo(set);
    }

    /// <summary>
    /// The resolution to decode the background at: the window's physical pixel size, which is by
    /// definition the most the display can show.
    /// </summary>
    private Vector2? backgroundDecodeSize()
    {
        if (window == null)
            return null;

        window.GetPhysicalSize(out int physicalWidth, out int physicalHeight);

        if (physicalWidth < 1 || physicalHeight < 1)
            return null;

        return new Vector2(physicalWidth, physicalHeight);
    }

    private void onDisableVideoChanged()
    {
        updateVideo(currentSet);
        updateVideoLayerAlpha();
    }

    private void updateVideoLayerAlpha()
    {
        bool shouldShow = !screenHideVideo
                          && !(disableVideo?.Value ?? false)
                          && !(gameplayDimActive && (disableGameplayVideo?.Value ?? false));

        if (shouldShow == videoShown)
            return;

        videoShown = shouldShow;

        // Cleared first because the framework retargets an in-flight transform in place and keeps its
        // original StartValue, so reversing this toggle before the fade finishes would animate from a
        // value to itself and snap. Every reversible fade in this class needs the same treatment.
        // Not propagated to children: the video container runs its own fade and must keep it.
        videoLayer.ClearTransforms();
        videoLayer.FadeTo(shouldShow ? 1f : 0f, fade_duration, Easing.OutQuint);
    }

    private void updateVideo(BeatmapSet? set)
    {
        if (currentVideoContainer.IsNotNull())
        {
            var previousContainer = currentVideoContainer;
            var previousVideo = currentVideo;
            previousContainer.FadeOut(fade_duration, Easing.OutQuint);
            Scheduler.AddDelayed(() =>
            {
                videoLayer.Remove(previousContainer);
                previousVideo?.Dispose();
            }, fade_duration);
            currentVideo = null;
            currentVideoContainer = null;
        }

        bool videoDisabled = disableVideo?.Value ?? false;

        if (set == null || !set.HasVideo || storage == null || string.IsNullOrEmpty(set.VideoStoragePath) || videoDisabled)
            return;

        currentVideo = new VideoSprite(storage.GetFullPath(set.VideoStoragePath))
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            FillMode = TextureFillMode.Fill
        };

        currentVideoContainer = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Alpha = 0,
            Child = currentVideo
        };

        videoLayer.Add(currentVideoContainer);
        currentVideoContainer.FadeIn(fade_duration, Easing.OutQuint);
    }

    /// <summary>
    /// The active audio channel driving playback — the gameplay channel in gameplay, else the preview.
    /// </summary>
    private IAudioChannel? activeChannel => audioManager?.InGameplay == true ? audioManager.GameplayChannel : audioManager?.CurrentChannel;

    /// <summary>
    /// Keeps the background video locked to the music. The video plays on its own clock, but is paused
    /// whenever the audio is and re-seeked only when it drifts past <see cref="resync_threshold"/> from
    /// the audio — one catch-up seek when a preview starts partway in, then nothing during steady
    /// playback. Small audio start-up jitter stays under the threshold, so it doesn't cause a burst of
    /// seeks. A paused video is still moved to the audio's position, so gameplay's count-in shows the
    /// song's first frame rather than wherever the preview had got to.
    /// </summary>
    private void updateVideoSync()
    {
        // Wait until the decoder is ready (Duration known) so a Seek actually takes effect.
        if (currentVideo.IsNull() || currentVideo.Duration <= 0)
            return;

        // Nobody sees a hidden video, so it is held still rather than decoded. Showing it again
        // catches it up with one seek.
        if (!videoShown && Precision.AlmostEqualZero(videoLayer.Alpha))
        {
            if (currentVideo.IsPlaying)
                currentVideo.Pause();
            return;
        }

        var channel = activeChannel;
        if (channel.IsNull())
            return;

        bool running = channel.IsRunning.Value;

        if (running && !currentVideo.IsPlaying)
            currentVideo.Play();
        else if (!running && currentVideo.IsPlaying)
            currentVideo.Pause();

        // Measured against where the video can actually be: past its end the video holds its last
        // frame, and comparing with the audio there would seek it again every frame.
        double target = Math.Min(channel.CurrentTime, currentVideo.Duration);

        if (Math.Abs(currentVideo.CurrentTime - target) > resync_threshold)
            currentVideo.Seek(target);
    }

    public override void Update()
    {
        base.Update();

        updateBlurTransition();
        updateVideoSync();
    }

    public void HideVideo()
    {
        screenHideVideo = true;
        updateVideoLayerAlpha();
    }

    public void ShowVideo()
    {
        screenHideVideo = false;
        updateVideoLayerAlpha();
    }

    /// <summary>
    /// Fades the dim overlay to the configured level when gameplay wants it, else clears it.
    /// </summary>
    private void updateDim()
    {
        // Deliberately does not clear in-flight transforms, unlike updateVideoLayerAlpha: the dim
        // slider drives this on every tick while it is dragged, and a fresh transform per tick would
        // restart a 500 ms ease each frame and never arrive. Retargeting in place is the correct
        // behaviour here. The discrete on/off case is handled by SetGameplayDim instead.
        float dim = gameplayDimActive ? Math.Clamp((backgroundDim?.Value ?? 0) / 100f, 0f, 1f) : 0f;
        dimBox.FadeTo(perceivedDimAlpha(dim), fade_duration, Easing.OutQuint);
    }

    /// <summary>
    /// The overlay alpha that makes the background look <paramref name="dim"/> darker. The renderers
    /// blend in linear light (sRGB framebuffers on GL and Metal), so a black overlay at alpha a leaves
    /// (1 − a)^(1/2.2) of the perceived brightness: 50% alpha only looked ~27% darker. Inverting that
    /// makes the setting read as it says — 50% is half as bright.
    /// </summary>
    private static float perceivedDimAlpha(float dim) => 1 - MathF.Pow(1 - dim, 2.2f);

    /// <summary>
    /// Applies the configured play background — dim, blur and whether the video shows — while the
    /// current screen wants it (gameplay, replays and the setup screen); clears all three otherwise.
    /// </summary>
    public void SetGameplayDim(bool active)
    {
        if (gameplayDimActive == active)
            return;

        gameplayDimActive = active;

        // A discrete toggle, so the fade has to play in full whichever way it is going — see
        // updateVideoLayerAlpha for why an in-flight transform must be cleared rather than retargeted.
        dimBox.ClearTransforms();
        updateDim();
        updateBlur();
        updateVideoLayerAlpha();
    }

    /// <summary>
    /// Whether the current screen permits background blur (combined with the user setting).
    /// </summary>
    public void SetBlurAllowed(bool allowed)
    {
        blurAllowed = allowed;
        updateBlur();
    }

    /// <summary>
    /// Temporarily forces the blur off without touching the setting — used while the UI is hidden to
    /// peek at the background, so the peek reveals a sharp image.
    /// </summary>
    public void SetBlurSuppressed(bool suppressed)
    {
        blurSuppressed = suppressed;
        updateBlur();
    }

    private void updateBlur()
    {
        if (gameplayDimActive)
        {
            // Play screens use the gameplay blur amount, not song select's on/off blur.
            targetBlurStrength = Math.Clamp((gameplayBlur?.Value ?? 0) / 100f, 0f, 1f) * gameplay_blur_max_strength;
            return;
        }

        bool on = blurAllowed && !blurSuppressed && (blurBackground?.Value ?? false);
        targetBlurStrength = on ? blur_strength : 0f;
    }

    private void updateBlurTransition()
    {
        if (currentBlurStrength == targetBlurStrength)
            return;

        double dt = Clock.ElapsedFrameTime;
        if (dt <= 0)
            dt = 1;

        currentBlurStrength += (targetBlurStrength - currentBlurStrength) * (1 - MathF.Exp(-0.015f * (float)dt));

        if (Math.Abs(currentBlurStrength - targetBlurStrength) < 0.05f)
            currentBlurStrength = targetBlurStrength;

        blurContainer.BlurSigma = new Vector2(currentBlurStrength);

        // Heavier blur at lower resolution: the loss is invisible under it, and the kernel's tap count
        // (3× sigma, compensated for scale) drops with it — gameplay blur goes up to sigma 25.
        blurContainer.FrameBufferScale = currentBlurStrength > 10 ? new Vector2(0.25f)
            : currentBlurStrength > 0.5f ? new Vector2(0.5f)
            : Vector2.One;
    }
}
