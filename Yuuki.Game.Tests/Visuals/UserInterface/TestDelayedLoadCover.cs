// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestDelayedLoadCover : TestScene
{
    private readonly HeadlessTextureManager textureManager = new HeadlessTextureManager();

    [BackgroundDependencyLoader]
    private void load()
    {
        // Provide a texture manager so the cover's async decode/upload path can run headless.
        Cache<ITextureManager>(textureManager);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        textureManager.Dispose();
    }

    [Test]
    public void TestAsyncLoadFadesIn()
    {
        DelayedLoadCover cover = null!;
        TestCoverSource source = null!;

        AddStep("add cover", () => addCover(cover = new DelayedLoadCover
        {
            Size = new Vector2(240),
            RelativeSizeAxes = Axes.None,
            PlaceholderColor = Color.SlateGray
        }));

        AddAssert("placeholder only", () => !cover.HasCover);

        AddStep("set source", () => cover.SetSource(source = new TestCoverSource(create1X1Bmp())));

        AddUntilStep("cover loaded", () => cover.HasCover);
        AddAssert("stream was opened once", () => source.OpenCount == 1);
    }

    [Test]
    public void TestCachedSourceShownImmediately()
    {
        DelayedLoadCover cover = null!;

        AddStep("add cover", () => addCover(cover = new DelayedLoadCover
        {
            Size = new Vector2(240),
            RelativeSizeAxes = Axes.None
        }));

        AddStep("set cached source", () => cover.SetSource(new TestCoverSource(textureManager.WhitePixel)));
        AddAssert("cover shown immediately", () => cover.HasCover);
    }

    [Test]
    public void TestClearingSourceReturnsToPlaceholder()
    {
        DelayedLoadCover cover = null!;

        AddStep("add cover", () => addCover(cover = new DelayedLoadCover
        {
            Size = new Vector2(240),
            RelativeSizeAxes = Axes.None
        }));

        AddStep("set cached source", () => cover.SetSource(new TestCoverSource(textureManager.WhitePixel)));
        AddAssert("has cover", () => cover.HasCover);

        AddStep("clear source", () => cover.SetSource(null));
        AddAssert("back to placeholder", () => !cover.HasCover);
    }

    private void addCover(DelayedLoadCover cover)
    {
        Clear();
        Add(cover);
    }

    private class TestCoverSource : ICoverSource
    {
        private readonly byte[]? bytes;

        public int OpenCount { get; private set; }

        public TestCoverSource(byte[] bytes)
        {
            this.bytes = bytes;
        }

        public TestCoverSource(Texture cached)
        {
            Cached = cached;
        }

        public Texture? Cached { get; }

        public string? CacheKey => "test-cover";

        public Stream? Open()
        {
            OpenCount++;
            return bytes == null ? null : new MemoryStream(bytes);
        }

        public void OnLoaded(Texture texture)
        {
        }
    }

    /// <summary>
    /// Builds a valid 1×1 24-bit BMP in memory (can be test with ImageSharp in headless)
    /// </summary>
    private static byte[] create1X1Bmp()
    {
        byte[] bmp = new byte[58];

        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        writeInt(bmp, 2, 58); // total file size
        writeInt(bmp, 10, 54); // offset to pixel data

        writeInt(bmp, 14, 40); // DIB header size
        writeInt(bmp, 18, 1); // width
        writeInt(bmp, 22, 1); // height
        bmp[26] = 1; // colour planes
        bmp[28] = 24; // bits per pixel
        writeInt(bmp, 34, 4); // raw image size (one padded row)

        // Single pixel, stored BGR + one padding byte.
        bmp[54] = 0x20; // B
        bmp[55] = 0x40; // G
        bmp[56] = 0xC0; // R
        bmp[57] = 0x00; // padding

        return bmp;
    }

    private static void writeInt(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value & 0xFF);
        target[offset + 1] = (byte)((value >> 8) & 0xFF);
        target[offset + 2] = (byte)((value >> 16) & 0xFF);
        target[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
