// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Yuuki.Game.Resources;

namespace Yuuki.Game.Graphics.Rendering;

/// <summary>
/// The game's own fragment shaders (Resources/Shaders), built for whichever renderer draws them. Each pairs with the
/// framework's <c>shader.vert</c> and can <c>#include</c> the framework's shader files (<c>sh_Utils.glsl</c>, for
/// mask clipping), as the framework's own effect shaders do.
/// </summary>
/// <remarks>
/// Only from the draw thread. A shader that won't build is logged once and comes back null: whatever draws with it
/// is left out rather than breaking the frame.
/// </remarks>
public static class GameShaders
{
    private static readonly Storage game_shaders = new EmbeddedResourceStorage(YuukiResourceAssembly.ResourceAssembly, "Yuuki.Game.Resources.Shaders");

    private static readonly Dictionary<string, IShader?> shaders = new Dictionary<string, IShader?>();
    private static IRenderer? shaderRenderer;

    /// <summary>
    /// The shader drawing with <paramref name="fragmentPath"/> on <paramref name="renderer"/>, built the first time
    /// it's asked for; null if it won't build.
    /// </summary>
    public static IShader? Get(ICustomShaderRenderer renderer, string fragmentPath)
    {
        // A new renderer (the backend switched) needs its own; the old ones went with the old renderer.
        if (!ReferenceEquals(renderer, shaderRenderer))
        {
            shaders.Clear();
            shaderRenderer = renderer;
        }

        if (shaders.TryGetValue(fragmentPath, out IShader? shader))
            return shader;

        try
        {
            shader = renderer.CreateShader(Sources(renderer.ShaderStorage), "shader.vert", fragmentPath);
        }
        catch (Exception e)
        {
            Logger.Error($"Couldn't build the {fragmentPath} shader, so what draws with it is left out.", e);
            shader = null;
        }

        shaders[fragmentPath] = shader;
        return shader;
    }

    /// <summary>
    /// Where a game shader's files are read from: the game's shaders, then <paramref name="frameworkShaders"/> (for the
    /// files they pair with and include).
    /// </summary>
    public static Storage Sources(Storage frameworkShaders) => new LayeredStorage(game_shaders, frameworkShaders);

    /// <summary>
    /// Reads from the game's shaders first and the framework's after, so a game shader finds the framework's files it
    /// pairs with and includes. Read only.
    /// </summary>
    private sealed class LayeredStorage : Storage
    {
        private readonly Storage first;
        private readonly Storage then;

        public LayeredStorage(Storage first, Storage then)
            : base(first.GetFullPath(string.Empty))
        {
            this.first = first;
            this.then = then;
        }

        private Storage holding(string path) => first.Exists(path) ? first : then;

        public override bool Exists(string path) => first.Exists(path) || then.Exists(path);

        public override bool ExistsDirectory(string path) => first.ExistsDirectory(path) || then.ExistsDirectory(path);

        public override string GetFullPath(string path, bool createIfNotExists = false) => holding(path).GetFullPath(path);

        public override IEnumerable<string> GetFiles(string path, string searchPattern = "*") =>
            first.GetFiles(path, searchPattern).Union(then.GetFiles(path, searchPattern));

        public override IEnumerable<string> GetDirectories(string path) => first.GetDirectories(path).Union(then.GetDirectories(path));

        public override Stream GetStream(string path, FileAccess access = FileAccess.Read, FileMode mode = FileMode.OpenOrCreate)
        {
            if (access != FileAccess.Read)
                throw new NotSupportedException("Shaders are read only.");

            return holding(path).GetStream(path, access, mode);
        }

        public override void Move(string fromPath, string toPath) => throw new NotSupportedException("Shaders are read only.");

        public override void Delete(string path) => throw new NotSupportedException("Shaders are read only.");

        public override void DeleteDirectory(string path) => throw new NotSupportedException("Shaders are read only.");

        public override bool OpenFileExternally(string filename) => false;

        public override bool PresentFileExternally(string filename) => false;
    }
}
