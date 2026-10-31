// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Testing;
using Yuuki.Game.Graphics.Rendering;
using Yuuki.Game.Resources;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// The game's shaders build and link for every backend
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

    [Test]
    public void TestTheShadersAreFound() =>
        Assert.That(shaders, Is.EquivalentTo(new[] { "bubble-back.frag", "bubble-front.frag", "edge-glow.frag", "full-bloom.frag", "landing-waves.frag", "rays.frag" }));

    [Test]
    public void TestEachWorksOnEveryBackend([ValueSource(nameof(shaders))] string shader) =>
        Assert.That(ShaderChecks.Problems(GameShaders.STORAGE, shader), Is.Empty);
}
