// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Reflection;
using Sakura.Framework;

namespace Yuuki.Game;

public static class BuildInfo
{
    private const string development_version = "1.0.0";

    /// <summary>
    /// The game's version, e.g. "2026.1014.0", or "development" for a local build.
    /// </summary>
    public static string GameVersion => versionOf(typeof(BuildInfo).Assembly);

    /// <summary>
    /// The Sakura framework's version, or "development" when it's a local project reference.
    /// </summary>
    public static string FrameworkVersion => versionOf(typeof(App).Assembly);

    /// <summary>
    /// The short commit the game was built from, when the SDK recorded one.
    /// </summary>
    public static string? Commit
    {
        get
        {
            string? informational = typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            int plus = informational?.IndexOf('+') ?? -1;

            if (informational == null || plus < 0)
                return null;

            string hash = informational[(plus + 1)..];
            return hash.Length > 7 ? hash[..7] : hash;
        }
    }

    public static string Configuration =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    /// <summary>
    /// When the game's assembly was built (its file's time), if it can be told.
    /// </summary>
    public static DateTime? BuildTime
    {
        get
        {
            string location = typeof(BuildInfo).Assembly.Location;

            // Single-file publishes have no location on disk.
            if (string.IsNullOrEmpty(location) || !File.Exists(location))
                return null;

            return File.GetLastWriteTime(location);
        }
    }

    private static string versionOf(Assembly assembly)
    {
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        string version = informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString(3) ?? development_version;

        return version == development_version ? "development" : version;
    }
}
