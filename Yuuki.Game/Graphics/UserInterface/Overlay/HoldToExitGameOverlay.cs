// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Yuuki.Game.Audio;

namespace Yuuki.Game.Graphics.UserInterface.Overlay;

/// <summary>
/// The black that comes down over the game as it quits
/// </summary>
public partial class HoldToExitGameOverlay : Container
{
    /// <summary>
    /// How long Escape has to be held
    /// </summary>
    public const double HOLD_DURATION = 200;

    /// <summary>
    /// How dark a finished hold leaves the screen.
    /// </summary>
    private const float hold_darkness = 0.7f;

    /// <summary>
    /// A released hold stays where it got to this long before lifting.
    /// </summary>
    private const double release_delay = 200;

    private const double release_duration = 200;

    /// <summary>
    /// With the goodbye, the music first drops to this almost at once.
    /// </summary>
    private const float outro_music_dip = 0.03f;

    private const double outro_music_dip_duration = 200;

    [Resolved(canBeNull: true)]
    private YuukiAudioManager? audioManager { get; set; }

    [Resolved(canBeNull: true)]
    private IAudioStore<ISample>? sampleStore { get; set; }

    private ISample? goodbye;

    private readonly Box box;

    /// <summary>
    /// Run once a hold completes.
    /// </summary>
    public Action? Action;

    /// <summary>
    /// Whether Escape is being held toward quitting.
    /// </summary>
    public bool Holding { get; private set; }

    /// <summary>
    /// Whether the game is on its way out; the overlay only darkens from here.
    /// </summary>
    public bool Exiting { get; private set; }

    /// <summary>
    /// How dark the screen is, 0 to 1.
    /// </summary>
    public float Darkness { get; private set; }

    // The darkness ease in progress, and what to do when it arrives.
    private float easeFrom;
    private float easeTarget;
    private double easeStart;
    private double easeDuration;
    private Easing easing;
    private Action? onArrived;

    // Whether the music is turned down, so it's put back once when the black lifts.
    private bool ducking;

    // Quitting with the goodbye: the music follows its own curve rather than the black, starting
    // from where it was.
    private bool outro;
    private float outroMusicFrom;

    public HoldToExitGameOverlay()
    {
        RelativeSizeAxes = Axes.Both;

        // Present while clear, so Update keeps running to start the next ease.
        AlwaysPresent = true;

        Child = box = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Bleed = Edges.All,
            Color = Color.Black,
            Alpha = 0
        };
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        // Loaded up front, so quitting doesn't wait on a disk read.
        if (sampleStore.IsNotNull())
            goodbye = sampleStore!.Get("UI/goodbye.wav");
    }

    // Purely a picture: holding is driven by the main menu, which owns the Escape key.
    public override bool HandlePositionalInput => false;

    public override bool HandleNonPositionalInput => false;

    /// <summary>Escape went down: the screen starts going dark, and quits if it gets all the way.</summary>
    public void BeginHold()
    {
        if (Holding || Exiting)
            return;

        Holding = true;

        // Picks up from wherever an earlier, released hold had got to.
        easeTo(hold_darkness, HOLD_DURATION * (1 - Darkness / hold_darkness), Easing.OutQuad, 0, () =>
        {
            Holding = false;
            Action?.Invoke();
        });
    }

    /// <summary>Escape came up before the hold finished: the black lifts again.</summary>
    public void AbortHold()
    {
        if (!Holding)
            return;

        Holding = false;
        easeTo(0, release_duration, Easing.InSine, release_delay, null);
    }

    /// <summary>
    /// Plays the goodbye for quitting. False when there's nothing to play (no sample store, as in
    /// headless tests), so the caller can take the quick way out instead.
    /// </summary>
    public bool PlayGoodbye()
    {
        if (goodbye == null)
            return false;

        IAudioChannel channel = goodbye.GetChannel();
        if (channel.IsNull())
            return false;

        channel.AutoDispose = true;
        channel.Play();
        return true;
    }

    /// <summary>
    /// Takes the screen and the music all the way down, then runs <paramref name="then"/>. With the
    /// goodbye playing over it (<paramref name="withGoodbye"/>)
    /// </summary>
    public void FadeOutGame(double duration, Action then, bool withGoodbye = false)
    {
        if (Exiting)
            return;

        Exiting = true;
        Holding = false;
        outro = withGoodbye;
        outroMusicFrom = 1 - Darkness;
        easeTo(1, duration, withGoodbye ? Easing.None : Easing.OutQuad, 0, then);
    }

    private void easeTo(float target, double duration, Easing ease, double delay, Action? arrived)
    {
        easeFrom = Darkness;
        easeTarget = target;
        easeStart = Clock.CurrentTime + delay;
        easeDuration = Math.Max(duration, 0);
        easing = ease;
        onArrived = arrived;
    }

    public override void Update()
    {
        base.Update();

        double elapsed = Clock.CurrentTime - easeStart;
        double progress = easeDuration > 0 ? Math.Clamp(elapsed / easeDuration, 0, 1) : elapsed >= 0 ? 1 : 0;

        Darkness = easeFrom + (easeTarget - easeFrom) * (float)EasingFunctions.Apply(easing, progress);

        // Snapped at the ends: the framework skips an alpha change under 0.0001, which would leave a
        // lifted overlay a hair short of clear and still drawn.
        box.Alpha = Darkness <= 0.001f ? 0 : Darkness;

        updateMusicVolume();

        if (progress >= 1 && onArrived != null)
        {
            var arrived = onArrived;
            onArrived = null;
            arrived();
        }
    }

    private void updateMusicVolume()
    {
        var channel = audioManager?.CurrentChannel;
        if (channel == null)
            return;

        if (outro)
        {
            channel.Volume.Value = outroMusicVolume();
            ducking = true;
        }
        else if (box.Alpha > 0)
        {
            channel.Volume.Value = 1 - box.Alpha;
            ducking = true;
        }
        else if (ducking)
        {
            channel.Volume.Value = 1;
            ducking = false;
        }
    }

    /// <summary>
    /// The music's volume under the goodbye: down to the dip, then easing out to silence by the end.
    /// </summary>
    private float outroMusicVolume()
    {
        double elapsed = Math.Max(0, Clock.CurrentTime - easeStart);

        if (elapsed < outro_music_dip_duration)
            return outroMusicFrom + (outro_music_dip - outroMusicFrom) * (float)(elapsed / outro_music_dip_duration);

        double rest = Math.Max(1, easeDuration - outro_music_dip_duration);
        double progress = Math.Clamp((elapsed - outro_music_dip_duration) / rest, 0, 1);
        return outro_music_dip * (1 - (float)EasingFunctions.Apply(Easing.InQuad, progress));
    }
}
