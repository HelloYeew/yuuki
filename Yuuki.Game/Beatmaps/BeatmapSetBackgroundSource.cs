// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// An <see cref="ICoverSource"/> that loads a beatmap set's background from local game storage.
/// </summary>
public class BeatmapSetBackgroundSource : ICoverSource
{
    private readonly BeatmapSet beatmapSet;
    private readonly Storage? storage;

    public BeatmapSetBackgroundSource(BeatmapSet beatmapSet, Storage? storage)
    {
        this.beatmapSet = beatmapSet;
        this.storage = storage;
    }

    // Non-null only for the bundled theme (pre-resolved). Disk sets return null -> decoded + evicted.
    public Texture? Cached => beatmapSet.Background;

    // The file as well as the set, as for covers: a changed background is a different image.
    public string? CacheKey => $"beatmap-background-{beatmapSet.ID}-{beatmapSet.BackgroundStoragePath}";

    public Stream? Open()
    {
        if (storage == null || string.IsNullOrEmpty(beatmapSet.BackgroundStoragePath))
            return null;

        return storage.GetStream(beatmapSet.BackgroundStoragePath);
    }

    public void OnLoaded(Texture texture)
    {
        // intentionally not stored on the model for disk sets, the loader owns and evicts the texture.
    }
}
