// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;

namespace Yuuki.Game.Tests;

/// <summary>
/// Writes small, real WAV files for tests that need a song on disk
/// </summary>
public static class TestWav
{
    public static string Write(string folder, string name, double seconds = 1, double frequency = 440)
    {
        const int sample_rate = 8000;
        int samples = (int)(seconds * sample_rate);
        string path = Path.Combine(folder, name);

        using var writer = new BinaryWriter(File.Create(path));

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + samples * 2);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sample_rate);
        writer.Write(sample_rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(samples * 2);

        for (int i = 0; i < samples; i++)
            writer.Write((short)(Math.Sin(2 * Math.PI * frequency * i / sample_rate) * 8000));

        return path;
    }
}
