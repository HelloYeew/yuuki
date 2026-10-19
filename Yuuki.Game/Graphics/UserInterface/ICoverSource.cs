// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using Sakura.Framework.Graphics.Textures;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// Describes where a <see cref="DelayedLoadCover"/> gets its image from. Implementations can pull
/// from local storage, a web request, an archive, an embedded resource, etc. The component only
/// cares about being handed an encoded image stream (and, optionally, an already-decoded texture).
/// </summary>
/// <remarks>
/// <see cref="Open"/> is called on a background load thread, so it is safe (and expected) to perform
/// blocking I/O such as reading a file or downloading from a server there.
/// </remarks>
public interface ICoverSource
{
    /// <summary>
    /// An already-decoded texture to display immediately, or <c>null</c> to load it via
    /// <see cref="Open"/>. Lets callers cache the result and skip re-decoding.
    /// </summary>
    Texture? Cached { get; }

    /// <summary>
    /// A stable key identifying this cover, used for GPU-texture de-duplication. May be <c>null</c>.
    /// </summary>
    string? CacheKey { get; }

    /// <summary>
    /// Opens a stream to the encoded image bytes. Called on a background thread. Returns <c>null</c>
    /// when the image is unavailable (e.g. missing file / failed request), in which case the cover
    /// simply stays on its placeholder.
    /// </summary>
    Stream? Open();

    /// <summary>
    /// Invoked on the update thread once the texture has been decoded and uploaded, letting the
    /// source cache it for next time. No-op by default for sources that don't cache.
    /// </summary>
    void OnLoaded(Texture texture);
}
