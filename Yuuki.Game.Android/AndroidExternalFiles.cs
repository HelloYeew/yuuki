// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using Android.Content;
using Android.Provider;
using Yuuki.Game.Database;
using Uri = Android.Net.Uri;

namespace Yuuki.Game.Android;

/// <summary>
/// content:// links, the way Android hands over a file: from "open with", a share, or the file picker. They're
/// read through the content resolver, with the permission the link came with.
/// </summary>
public class AndroidExternalFiles : IExternalFiles
{
    private readonly Context context;

    public AndroidExternalFiles(Context context)
    {
        this.context = context;
    }

    public bool IsLink(string path) => path.StartsWith("content://", StringComparison.Ordinal);

    public string NameOf(string link)
    {
        var uri = Uri.Parse(link)!;

        // The app that handed the link over decides what it answers: no permission to read it (one sent without the
        // grant, or a stale one from Recents), no such column, or a file deleted since all throw, and this is asked on the
        // update thread. The link's own last segment names it then, and opening it is where the import fails.
        try
        {
            using (var cursor = context.ContentResolver?.Query(uri, new[] { IOpenableColumns.DisplayName }, null, null, null))
            {
                if (cursor != null && cursor.MoveToFirst() && cursor.GetString(0) is { Length: > 0 } name)
                    return name;
            }
        }
        catch (Exception)
        {
        }

        return uri.LastPathSegment ?? link;
    }

    public Stream Open(string link)
    {
        // Copied out first: the resolver's stream only reads forwards, and an archive is read from all over.
        string copy = Path.Combine(context.CacheDir!.AbsolutePath, "import-" + Guid.NewGuid().ToString("N"));

        using (var source = context.ContentResolver!.OpenInputStream(Uri.Parse(link)!) ?? throw new FileNotFoundException("The file can't be opened.", link))
        using (var target = File.Create(copy))
            source.CopyTo(target);

        return new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
    }
}
