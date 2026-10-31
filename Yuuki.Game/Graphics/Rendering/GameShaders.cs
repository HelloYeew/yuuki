// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Platform;
using Yuuki.Game.Resources;

namespace Yuuki.Game.Graphics.Rendering;

/// <summary>
/// The game's own shaders (Resources/Shaders), for a <see cref="CustomShaderDrawNode"/> to draw with.
/// </summary>
public static class GameShaders
{
    /// <summary>
    /// Where they are.
    /// </summary>
    public static readonly Storage STORAGE = new EmbeddedResourceStorage(YuukiResourceAssembly.ResourceAssembly, "Yuuki.Game.Resources.Shaders");

    /// <summary>
    /// Builds them, for whichever renderer draws them.
    /// </summary>
    public static readonly CustomShaderStore STORE = new CustomShaderStore(STORAGE);
}
