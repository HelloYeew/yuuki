// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Timing;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// Wires a <see cref="GameplayClock"/> into the scene graph. Everything nested inside runs on
/// gameplay time: the clock is advanced once per frame (before this container's own update and
/// before its children), and set as this container's <see cref="Sakura.Framework.Graphics.Drawables.Drawable.Clock"/>
/// so all descendants inherit it by reference.
/// <para>
/// Pass an audio source (e.g., a <see cref="Sakura.Framework.Audio.TrackClock"/>) for music-synced
/// play, or leave it null to run off a <see cref="StopwatchClock"/> for deterministic, audioless
/// prototype/test runs. The framework only auto-processes a custom clock when it is a
/// <see cref="FramedClock"/>; <see cref="GameplayClock"/> is not, so this type owns the per-frame
/// <see cref="GameplayClock.ProcessFrame"/> call.
/// </para>
/// </summary>
public partial class GameplayClockContainer : Container
{
    private readonly GameplayClock gameplayClock;

    /// <summary>
    /// The gameplay clock driving this subtree. Read <see cref="GameplayClock.CurrentTime"/> for the
    /// current time and use <see cref="GameplayClock.GetTimeAt"/> to judge input events.
    /// </summary>
    public GameplayClock GameplayClock => gameplayClock;

    /// <summary>
    /// Lead-in applied by <see cref="Restart"/>: gameplay starts this many ms before time 0.
    /// </summary>
    public double LeadInTime { get; set; } = 2000;

    public double CurrentTime => gameplayClock.CurrentTime;

    public bool IsRunning => gameplayClock.IsRunning;

    public double Rate
    {
        get => gameplayClock.Rate;
        set => gameplayClock.Rate = value;
    }

    public GameplayClockContainer(IAdjustableClock? source = null)
    {
        gameplayClock = new GameplayClock(source ?? new StopwatchClock());

        // Run the whole subtree on gameplay time. Descendants inherit this clock by reference.
        Clock = gameplayClock;
    }

    public override void UpdateSubTree()
    {
        // Advance the gameplay clock before anything in this subtree reads its time this frame.
        gameplayClock.ProcessFrame();
        base.UpdateSubTree();
    }

    /// <summary>
    /// Starts (or resumes) the clock from its current position.
    /// </summary>
    public void Start() => gameplayClock.Start();

    /// <summary>
    /// Pauses the clock, holding the current position.
    /// </summary>
    public void Stop() => gameplayClock.Stop();

    /// <summary>
    /// Seeks to an absolute gameplay time (maybe negative during lead-in).
    /// </summary>
    public bool Seek(double time)
    {
        bool success = gameplayClock.Seek(time);
        gameplayClock.ProcessFrame();
        return success;
    }

    /// <summary>
    /// Restarts from <c>-<see cref="LeadInTime"/></c> so notes scroll in before time 0.
    /// </summary>
    public void Restart()
    {
        gameplayClock.Stop();
        Seek(-LeadInTime);
        gameplayClock.Start();
    }
}
