// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Yuuki.Game.Graphics.Colors;

/// <summary>
/// Each cover's color, picked while the cover is decoded for display (see
/// <see cref="Yuuki.Game.Graphics.UserInterface.ICoverSource.PickColor"/>), so it costs no extra decode.
/// </summary>
/// <remarks>
/// Kept for the session only: picking is a small fraction of the decode the cover pays anyway, so a cache
/// file would save nothing worth its invalidation. Filled from load threads and read from the update
/// thread.
/// </remarks>
public class CoverColorStore
{
    private readonly ConcurrentDictionary<string, CoverSwatch> swatches = new ConcurrentDictionary<string, CoverSwatch>();

    private int version;

    /// <summary>
    /// Goes up every time a color is added. Something showing a colour that wasn't known yet watches this
    /// to know when to look again.
    /// </summary>
    public int Version => Volatile.Read(ref version);

    public bool Contains(string key) => swatches.ContainsKey(key);

    public bool TryGet(string key, out CoverSwatch swatch) => swatches.TryGetValue(key, out swatch);

    /// <summary>
    /// Picks the color of a cover from its decoded pixels and keeps it under <paramref name="key"/>.
    /// </summary>
    public void Pick(string key, ReadOnlySpan<byte> rgba, int width, int height)
    {
        swatches[key] = CoverColorExtractor.Extract(rgba, width, height);
        Interlocked.Increment(ref version);
    }
}
