// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Platform;
using Sakura.Framework.SPIRV;
using Yuuki.Game.Graphics.Rendering;
using Yuuki.Game.Resources;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// The game's shaders build for every backend
/// </summary>
[TestFixture]
public class GameShadersTest
{
    private const string shader_namespace = "Yuuki.Game.Resources.Shaders.";

    /// <summary>
    /// Every fragment shader the game has.
    /// </summary>
    private static readonly string[] shaders = YuukiResourceAssembly.ResourceAssembly.GetManifestResourceNames()
                                                                     .Where(n => n.StartsWith(shader_namespace, StringComparison.Ordinal) && n.EndsWith(".frag", StringComparison.Ordinal))
                                                                     .Select(n => n[shader_namespace.Length..])
                                                                     .OrderBy(n => n, StringComparer.Ordinal)
                                                                     .ToArray();

    private static readonly Storage framework_shaders = new EmbeddedResourceStorage(typeof(AppHost).Assembly, "Sakura.Framework.Resources.Shaders");

    [Test]
    public void TestTheShadersAreFound() =>
        Assert.That(shaders, Is.EquivalentTo(new[] { "bubble-back.frag", "bubble-front.frag", "edge-glow.frag", "full-bloom.frag", "landing-waves.frag", "rays.frag" }));

    [Test]
    public void TestEachBuilds([ValueSource(nameof(shaders))] string shader,
                               [Values(CrossCompileTarget.GLSL, CrossCompileTarget.HLSL, CrossCompileTarget.MSL)] CrossCompileTarget target) =>
        Assert.DoesNotThrow(() => ShaderCompiler.GetOrCompile(GameShaders.Sources(framework_shaders), "shader.vert", shader, target));

    [Test]
    public void TestEachBuildsForVulkan([ValueSource(nameof(shaders))] string shader) =>
        Assert.DoesNotThrow(() => ShaderCompiler.GetOrCompileSpirv(GameShaders.Sources(framework_shaders), "shader.vert", shader));

    /// <summary>
    /// Direct3D links the vertex shader's outputs to the fragment shader's inputs by register, not by name, and the
    /// compiler drops an input the shader never reads. One dropped would move every later one onto the wrong output,
    /// so each fragment shader must take all nine of shader.vert's.
    /// </summary>
    [Test]
    public void TestEachTakesEveryInputOnDirect3D([ValueSource(nameof(shaders))] string shader)
    {
        (_, string fragment) = ShaderCompiler.GetOrCompile(GameShaders.Sources(framework_shaders), "shader.vert", shader, CrossCompileTarget.HLSL);

        Assert.That(Enumerable.Range(0, 9).Where(i => !fragment.Contains($": TEXCOORD{i};")), Is.Empty, "inputs missing");
    }
}
