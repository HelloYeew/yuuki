// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Threading;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Textures;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// Covers let go of a moment ago, kept on the GPU a while longer, so showing one again (scrolling back up
/// the list, flipping back to a song in the focus window, coming back from playing it) shows it at once
/// instead of decoding it again and fading it in.
/// </summary>
/// <remarks>
/// <para>
/// Holds the reference to a shared cover texture (see <see cref="ITextureManager.ReleaseSharedTexture"/>)
/// that a <see cref="DelayedLoadCover"/> would otherwise have released, up to <see cref="Budget"/> bytes and
/// for at most <see cref="MaxAge"/>, the one let go longest ago released first. A cover shown again takes its
/// reference back out (<see cref="TryTake"/>), so what's held here is, nearly always, covers not on screen.
/// </para>
/// <para>
/// Covers are let go from the update thread and, for a load thrown away, from wherever the framework
/// disposes it, so this is locked.
/// </para>
/// </remarks>
public partial class RecentCoverTextures : Component
{
    /// <summary>
    /// How much (bytes, as uploaded) is kept. A song row's cover is 64 KB, the focus window's 256 KB.
    /// </summary>
    public long Budget { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// How long (ms) a cover is kept after it was let go.
    /// </summary>
    public double MaxAge { get; set; } = 60_000;

    /// <summary>
    /// The share of <see cref="Budget"/> one texture may take and still be kept. A full-screen background
    /// is let go at once: keeping it would push out a few hundred song covers.
    /// </summary>
    private const int max_share_of_budget = 8;

    private readonly Lock sync = new Lock();

    // Oldest first.
    private readonly LinkedList<Entry> entries = new LinkedList<Entry>();
    private readonly Dictionary<string, LinkedListNode<Entry>> byKey = new Dictionary<string, LinkedListNode<Entry>>();

    private long bytes;
    private bool disposed;

    /// <summary>
    /// How many covers are kept (diagnostics/tests).
    /// </summary>
    public int Count
    {
        get
        {
            lock (sync)
                return entries.Count;
        }
    }

    /// <summary>
    /// Whether a reference to the texture shared under <paramref name="shareKey"/> is kept (diagnostics/tests).
    /// </summary>
    public bool Holds(string shareKey)
    {
        lock (sync)
            return byKey.ContainsKey(shareKey);
    }

    /// <summary>
    /// Takes over one reference to the shared texture under <paramref name="shareKey"/>, which the caller
    /// was about to release.
    /// </summary>
    public void Retire(string shareKey, Texture texture, ITextureManager textureManager)
    {
        List<Entry>? toRelease = null;
        long textureBytes = (long)texture.Width * texture.Height * 4;

        lock (sync)
        {
            if (disposed || textureBytes > Budget / max_share_of_budget)
                toRelease = new List<Entry> { new Entry(shareKey, null, textureManager, 0, 0) };
            else
            {
                // Kept already (shown twice at once, say): the one held is enough, and counts as let go now.
                if (byKey.TryGetValue(shareKey, out var held))
                {
                    toRelease = new List<Entry> { new Entry(shareKey, null, textureManager, 0, 0) };
                    remove(held);
                }

                var node = entries.AddLast(new Entry(shareKey, texture, textureManager, textureBytes, Environment.TickCount64));
                byKey[shareKey] = node;
                bytes += node.Value.Bytes;

                while (bytes > Budget && entries.First != null)
                    (toRelease ??= new List<Entry>()).Add(remove(entries.First));
            }
        }

        release(toRelease);
    }

    /// <summary>
    /// Hands back the reference kept for <paramref name="shareKey"/>, if one is, to a cover showing it again.
    /// The caller now owns it, and releases (or retires) it as it would one it acquired.
    /// </summary>
    public bool TryTake(string shareKey, ITextureManager textureManager, out Texture texture)
    {
        lock (sync)
        {
            // A reference kept for one texture manager is no good to another.
            if (byKey.TryGetValue(shareKey, out var held) && held.Value.TextureManager == textureManager)
            {
                texture = remove(held).Texture!;
                return true;
            }
        }

        texture = null!;
        return false;
    }

    public override void Update()
    {
        base.Update();

        List<Entry>? toRelease = null;
        long expired = Environment.TickCount64 - (long)MaxAge;

        lock (sync)
        {
            while (entries.First != null && entries.First.Value.RetiredAt < expired)
                (toRelease ??= new List<Entry>()).Add(remove(entries.First));
        }

        release(toRelease);
    }

    private Entry remove(LinkedListNode<Entry> node)
    {
        entries.Remove(node);
        byKey.Remove(node.Value.Key);
        bytes -= node.Value.Bytes;
        return node.Value;
    }

    // Outside the lock: the last release of a texture disposes it.
    private static void release(List<Entry>? toRelease)
    {
        if (toRelease == null)
            return;

        foreach (var entry in toRelease)
            entry.TextureManager.ReleaseSharedTexture(entry.Key);
    }

    protected override void Dispose(bool isDisposing)
    {
        List<Entry> toRelease;

        lock (sync)
        {
            disposed = true;
            toRelease = new List<Entry>(entries);
            entries.Clear();
            byKey.Clear();
            bytes = 0;
        }

        release(toRelease);

        base.Dispose(isDisposing);
    }

    private readonly record struct Entry(string Key, Texture? Texture, ITextureManager TextureManager, long Bytes, long RetiredAt);
}
