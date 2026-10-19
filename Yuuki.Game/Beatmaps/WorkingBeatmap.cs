// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Logging;
using Sakura.Framework.Reactive;

namespace Yuuki.Game.Beatmaps;

public class WorkingBeatmap
{
    public readonly Reactive<Beatmap> ReactiveWorkingBeatmap = new Reactive<Beatmap>(null!);

    public readonly Reactive<BeatmapSet> ReactiveWorkingBeatmapSet = new Reactive<BeatmapSet>(null!);

    public WorkingBeatmap()
    {
        ReactiveWorkingBeatmap.ValueChanged += e => Logger.Verbose($"🎶 Working beatmap changed to {e.NewValue}");
        ReactiveWorkingBeatmapSet.ValueChanged += e => Logger.Verbose($"🎵 Working beatmapset changed to {e.NewValue}");
    }

    public Beatmap Beatmap
    {
        get => ReactiveWorkingBeatmap.Value;
        set => ReactiveWorkingBeatmap.Value = value;
    }

    public BeatmapSet BeatmapSet
    {
        get => ReactiveWorkingBeatmapSet.Value;
        set => ReactiveWorkingBeatmapSet.Value = value;
    }
}
