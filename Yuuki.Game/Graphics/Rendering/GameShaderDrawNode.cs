// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Uniforms;

namespace Yuuki.Game.Graphics.Rendering;

/// <summary>
/// Draws a drawable's vertices with one of the game's shaders (<see cref="GameShaders"/>) rather than the framework's:
/// a drawable returns one from <c>CreateDrawNode</c>. The shader takes what it needs from the vertices (each shader
/// says what its vertices carry), and stays inside any mask the drawable is in.
/// </summary>
/// <remarks>
/// Nothing is drawn where the shader can't be: headless, or where it won't build.
/// </remarks>
public class GameShaderDrawNode : DrawNode
{
    private readonly string fragmentPath;

    /// <param name="fragmentPath">The shader, in the game's Resources/Shaders.</param>
    public GameShaderDrawNode(string fragmentPath)
    {
        this.fragmentPath = fragmentPath;
    }

    /// <summary>
    /// A color as one number, six bits a channel (red highest), for a shader to unpack (<c>unpackColor</c> in yuuki.glsl):
    /// small enough to come through the GPU's interpolation exact. Six bits is plenty for a tint of light.
    /// </summary>
    public static float PackColor(Color color)
    {
        int channel(byte value) => (int)MathF.Round(value / 255f * 63);
        return channel(color.R) * 4096 + channel(color.G) * 64 + channel(color.B);
    }

    public override void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0 || VertexCount == 0 || renderer is not ICustomShaderRenderer custom)
            return;

        IShader? shader = GameShaders.Get(custom, fragmentPath);
        if (shader == null)
            return;

        renderer.SetBlendMode(Blending);
        renderer.FlushBatch();

        shader.Use();
        shader.SetUniformBlock("ProjectionBlock", new ProjectionBlock
        {
            Projection = renderer.ProjectionMatrix
        });

        renderer.ApplyCurrentClip(WritableVertices);
        custom.DrawVerticesRaw(Vertices);
        renderer.RestoreMainShader();
    }
}
