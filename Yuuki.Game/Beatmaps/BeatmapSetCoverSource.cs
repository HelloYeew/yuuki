// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// An <see cref="ICoverSource"/> that loads a beatmap set's cover from local game storage.
/// </summary>
public class BeatmapSetCoverSource : ICoverSource
{
    private readonly BeatmapSet beatmapSet;
    private readonly Storage storage;

    public BeatmapSetCoverSource(BeatmapSet beatmapSet, Storage storage)
    {
        this.beatmapSet = beatmapSet;
        this.storage = storage;
    }

    public Texture? Cached => null;

    public string? CacheKey => KeyFor(beatmapSet);

    public bool PickColor => true;

    /// <summary>
    /// The key a set's cover is shared and coloured under, for looking its colour up in the
    /// <see cref="Yuuki.Game.Graphics.Colors.CoverColorStore"/>.
    /// </summary>
    /// <remarks>
    /// The file as well as the set: a cover changed in the editor is a different image, not the one
    /// already shared under the set's ID.
    /// </remarks>
    public static string KeyFor(BeatmapSet set) => $"beatmap-cover-{set.ID}-{set.CoverStoragePath}";

    public Stream? Open()
    {
        if (storage.IsNull() || string.IsNullOrEmpty(beatmapSet.CoverStoragePath))
            return null;

        return storage.GetStream(beatmapSet.CoverStoragePath);
    }

    public void OnLoaded(Texture texture)
    {
        // intentionally not stored on the model for disk sets, the loader owns and evicts the texture.
    }
}
