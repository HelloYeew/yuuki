// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Sakura.Framework;
using Sakura.Framework.Android;

namespace Yuuki.Game.Android;

[Activity(
    Label = "yuuki.",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleInstance,
    Theme = "@android:style/Theme.Black.NoTitleBar",
    // Every layout in the game is a landscape one.
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    // Rotating, resizing or folding would otherwise restart the activity; the game lays itself out again instead. So would
    // changing the system's language, text size or bold text, display colors, or SIM: SDL ends the whole app when its
    // activity is made a second time, so each of those closed the game.
    ConfigurationChanges = ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation | ConfigChanges.Orientation
                           | ConfigChanges.ScreenLayout | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode
                           | ConfigChanges.Density | ConfigChanges.Locale | ConfigChanges.LayoutDirection | ConfigChanges.FontScale
                           | ConfigChanges.FontWeightAdjustment | ConfigChanges.GrammaticalGender | ConfigChanges.ColorMode
                           | ConfigChanges.Touchscreen | ConfigChanges.Mcc | ConfigChanges.Mnc)]
// An .osz a file manager or browser opens with the game (most give it no type of its own, so its name says what it is),
// or one shared to it. Android's path patterns don't backtrack: ".*" runs to the first "." and needs ".osz" right
// there, so a name with dots before it ("(TV size ver.).osz") needs a pattern with as many.
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "content", DataMimeType = "application/x-osu-beatmap-archive")]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "content", DataHost = "*", DataMimeType = "*/*",
    DataPathPatterns = new[] { ".*\\\\.osz", ".*\\\\..*\\\\.osz", ".*\\\\..*\\\\..*\\\\.osz", ".*\\\\..*\\\\..*\\\\..*\\\\.osz" })]
[IntentFilter(new[] { Intent.ActionSend, Intent.ActionSendMultiple }, Categories = new[] { Intent.CategoryDefault },
    DataMimeTypes = new[] { "application/x-osu-beatmap-archive", "application/zip", "application/x-zip-compressed", "application/octet-stream" })]
public class MainActivity : SakuraActivity
{
    private readonly object gameLock = new object();
    private YuukiGame? game;

    // Files handed over before the game was made, for it to import once it is.
    private readonly List<string> pendingImports = new List<string>();

    protected override string AppName => "yuuki";

    protected override App CreateApp()
    {
        lock (gameLock)
        {
            game = new YuukiGame { ExternalFiles = new AndroidExternalFiles(this) };

            if (pendingImports.Count > 0)
                game.ImportShared(pendingImports.ToArray());

            pendingImports.Clear();
            return game;
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Brought back after the system let it go, it's handed the intent it started with again.
        if (savedInstanceState == null)
            importFrom(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        importFrom(intent);
    }

    private void importFrom(Intent? intent)
    {
        var links = new List<string>();

        switch (intent?.Action)
        {
            case Intent.ActionView when intent.Data != null:
                links.Add(intent.Data.ToString()!);
                break;

            // A share's files are its clip data, one or many.
            case Intent.ActionSend:
            case Intent.ActionSendMultiple:
                for (int i = 0; i < (intent.ClipData?.ItemCount ?? 0); i++)
                {
                    if (intent.ClipData!.GetItemAt(i)?.Uri is { } uri)
                        links.Add(uri.ToString()!);
                }

                break;
        }

        if (links.Count == 0)
            return;

        lock (gameLock)
        {
            if (game != null)
                game.ImportShared(links);
            else
                pendingImports.AddRange(links);
        }
    }
}
