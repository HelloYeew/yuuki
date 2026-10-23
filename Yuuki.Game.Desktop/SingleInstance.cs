// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Sakura.Framework.Development;
using Sakura.Framework.Logging;

namespace Yuuki.Game.Desktop;

/// <summary>
/// Keeps the game to one copy for each player. Opening an .osz from the file manager while the game's running starts
/// it again, and that copy hands the file to the one running and leaves, rather than playing on the same library.
/// </summary>
/// <remarks>
/// The first copy holds a named mutex for as long as it runs, so the next can tell straight away, and listens on a
/// named pipe, which a later copy writes its files to, a path a line. Both are the player's own: another user on the
/// same computer runs a copy of their own.
/// </remarks>
internal sealed class SingleInstance : IDisposable
{
    // Development builds keep to themselves, so one can run beside the installed game. The player's name keeps the
    // pipe theirs where every user's are side by side (Windows, and Linux's /tmp).
    private static readonly string name = "dev.helloyeew.yuuki" + (DebugUtils.IsDebugBuild ? ".development" : string.Empty) + "." + Environment.UserName;

    /// <summary>
    /// How long a later copy waits for the first to take its files: the first may only just be starting.
    /// </summary>
    private const int hand_over_timeout_ms = 5000;

    private readonly Mutex mutex;
    private readonly CancellationTokenSource listening = new CancellationTokenSource();

    private SingleInstance(Mutex mutex)
    {
        this.mutex = mutex;
    }

    /// <summary>
    /// Makes this the copy that runs, or returns null where another already is.
    /// </summary>
    public static SingleInstance? Claim()
    {
        // The session too on Windows, where everything a player opens runs in theirs. Elsewhere a session is narrower: on
        // macOS what the Finder opens and what a terminal does are in different ones.
        var mutex = new Mutex(false, name, new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = OperatingSystem.IsWindows() });

        try
        {
            if (mutex.WaitOne(0))
                return new SingleInstance(mutex);
        }
        catch (AbandonedMutexException)
        {
            // Left held by a copy that crashed: it's this one's now.
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>
    /// Gives <paramref name="files"/> to the copy that's running. False if it couldn't be reached.
    /// </summary>
    public static bool HandOver(IReadOnlyList<string> files)
    {
        try
        {
            using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly))
            {
                pipe.Connect(hand_over_timeout_ms);

                using (var writer = new StreamWriter(pipe))
                {
                    foreach (string file in files)
                        writer.WriteLine(file);
                }
            }

            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Takes the files later copies hand over, until this is disposed. <paramref name="received"/> is called off the
    /// main thread.
    /// </summary>
    public void Listen(Action<IReadOnlyList<string>> received) => Task.Run(() => listen(received, listening.Token));

    private async Task listen(Action<IReadOnlyList<string>> received, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await using (var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
                {
                    await pipe.WaitForConnectionAsync(cancellation).ConfigureAwait(false);

                    var files = new List<string>();

                    using (var reader = new StreamReader(pipe))
                    {
                        while (await reader.ReadLineAsync(cancellation).ConfigureAwait(false) is string line)
                        {
                            if (line.Length > 0)
                                files.Add(line);
                        }
                    }

                    received(files);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                Logger.Error("Files from another copy of the game couldn't be taken", e);

                // Not round again at once, for a pipe that keeps failing.
                if (cancellation.WaitHandle.WaitOne(1000))
                    return;
            }
        }
    }

    // On the thread that claimed it, as a mutex is released.
    public void Dispose()
    {
        listening.Cancel();

        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
