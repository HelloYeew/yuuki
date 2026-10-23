// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OsuParsers.Beatmaps.Objects.Mania;
using OsuParsers.Decoders;
using OsuParsers.Enums;
using OsuParsers.Enums.Beatmaps;
using Sakura.Framework.Logging;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Beatmaps.ControlPoints;
using Yuuki.Game.Beatmaps.Difficulty;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Utilities;
using AtlTrack = ATL.Track;
using OsuBeatmap = OsuParsers.Beatmaps.Beatmap;
using YuukiTimeSignature = Yuuki.Game.Beatmaps.TimeSignature;

namespace Yuuki.Game.Database;

/// <summary>
/// Turns an osu! .osz into a yuuki beatmap set folder
/// </summary>
public class OszImporter
{
    private readonly string beatmapsFolder;

    /// <param name="beatmapsFolder">The library's beatmaps folder, as a file system path.</param>
    public OszImporter(string beatmapsFolder)
    {
        this.beatmapsFolder = beatmapsFolder;
    }

    /// <summary>
    /// Imports one .osz.
    /// </summary>
    public OszImportResult Import(string oszPath)
    {
        if (!File.Exists(oszPath))
            return OszImportResult.Failed("The file isn't there any more.");

        return import(Path.GetFileName(oszPath), extractPath => ZipFile.ExtractToDirectory(oszPath, extractPath));
    }

    /// <summary>
    /// Imports one .osz read from <paramref name="osz"/>, for a file with no path of its own (one a phone hands
    /// over as a link). <paramref name="fileName"/> is its name, with its extension.
    /// </summary>
    public OszImportResult Import(Stream osz, string fileName) =>
        import(fileName, extractPath => ZipFile.ExtractToDirectory(osz, extractPath));

    private OszImportResult import(string fileName, Action<string> extract)
    {
        string extractPath = Path.Combine(Path.GetTempPath(), "yuuki-osz-" + Guid.NewGuid().ToString("N"));

        try
        {
            extract(extractPath);
            return importExtracted(extractPath, Path.GetFileNameWithoutExtension(fileName));
        }
        catch (Exception e)
        {
            Logger.Error($"Couldn't import {fileName}.", e);
            return OszImportResult.Failed(e.Message);
        }
        finally
        {
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);
        }
    }

    private OszImportResult importExtracted(string extractPath, string oszName)
    {
        List<OsuBeatmap> difficulties = readManiaDifficulties(extractPath);

        if (difficulties.Count == 0)
            return OszImportResult.Failed("It has no osu!mania difficulty yuuki can play (4K to 8K).");

        OsuBeatmap first = difficulties[0];

        string audioFile = first.GeneralSection.AudioFilename;
        string audioSource = findInArchive(extractPath, audioFile)
                             ?? throw new FileNotFoundException($"The audio file \"{audioFile}\" isn't in the .osz.");

        // Not every difficulty has to set the background or video; take the first one that does.
        string? backgroundSource = difficulties.Select(d => findInArchive(extractPath, d.EventsSection.BackgroundImage)).FirstOrDefault(p => p != null);
        string? videoSource = difficulties.Select(d => findInArchive(extractPath, d.EventsSection.Video)).FirstOrDefault(p => p != null);

        var metadata = first.MetadataSection;
        string title = firstNonEmpty(metadata.Title, metadata.TitleUnicode);
        string artist = firstNonEmpty(metadata.Artist, metadata.ArtistUnicode);

        int setId = metadata.BeatmapSetID > 0
            ? metadata.BeatmapSetID
            : idFromOszName(oszName) ?? generatedId("set", artist, title, metadata.Creator);

        var beatmapSet = new BeatmapSet
        {
            ID = setId,
            Title = title,
            TitleUnicode = firstNonEmpty(metadata.TitleUnicode, title),
            Artist = artist,
            ArtistUnicode = firstNonEmpty(metadata.ArtistUnicode, artist),
            Source = metadata.Source ?? "",
            SourceUnicode = metadata.Source ?? "",
            // osu! keeps genre on the website, not in the .osu.
            Genre = "",
            Creator = metadata.Creator ?? "",
            HasVideo = videoSource != null,
            CoverPath = backgroundSource == null ? "" : Path.GetFileName(backgroundSource),
            TrackPath = Path.GetFileName(audioSource),
            BackgroundPath = backgroundSource == null ? "" : Path.GetFileName(backgroundSource),
            VideoPath = videoSource == null ? "" : Path.GetFileName(videoSource)
        };

        var beatmaps = new List<Beatmap>();
        var skipped = new List<string>();

        foreach (OsuBeatmap difficulty in difficulties)
        {
            string name = difficulty.MetadataSection.Version;

            if (!string.Equals(difficulty.GeneralSection.AudioFilename, audioFile, StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add($"[{name}] uses a different audio file");
                continue;
            }

            int laneCount = laneCountOf(difficulty);
            BeatmapNote[] notes = convertNotes(difficulty, laneCount);

            beatmaps.Add(new Beatmap
            {
                ID = difficulty.MetadataSection.BeatmapID > 0 ? difficulty.MetadataSection.BeatmapID : generatedId("beatmap", setId.ToString(), name),
                BeatmapSet = beatmapSet,
                Creator = difficulty.MetadataSection.Creator ?? "",
                DifficultyName = name,
                DifficultyRating = OsuManiaStarRating.Calculate(notes, laneCount),
                LaneCount = laneCount,
                ControlPoints = timingOf(difficulty),
                BackgroundPath = beatmapSet.BackgroundPath,
                Notes = notes.OrderBy(n => n.StartTime).ThenBy(n => n.Lane).ToArray()
            });
        }

        double lastNoteTime = beatmaps.SelectMany(b => b.Notes).Select(n => n.EndTime).DefaultIfEmpty(0).Max();
        double trackLength = new AtlTrack(audioSource).DurationMs;

        beatmapSet.TotalLength = (int)(trackLength > 0 ? trackLength : lastNoteTime);
        // osu! writes -1 when the mapper didn't pick a preview point; osu! then plays from 40% in.
        beatmapSet.PreviewTime = first.GeneralSection.PreviewTime >= 0 ? first.GeneralSection.PreviewTime : (int)(beatmapSet.TotalLength * 0.4);
        beatmapSet.BPM = mostCommonBpm(first, lastNoteTime);

        write(beatmapSet, beatmaps, audioSource, backgroundSource, videoSource);

        foreach (string reason in skipped)
            Logger.Warning($"Import of {beatmapSet}: skipped {reason}.");

        return OszImportResult.Imported(beatmapSet, beatmaps.Count);
    }

    private static List<OsuBeatmap> readManiaDifficulties(string extractPath)
    {
        var difficulties = new List<OsuBeatmap>();

        foreach (string osuFile in Directory.EnumerateFiles(extractPath, "*.osu").Order(StringComparer.OrdinalIgnoreCase))
        {
            OsuBeatmap beatmap;

            try
            {
                beatmap = BeatmapDecoder.Decode(osuFile);
            }
            catch (Exception e)
            {
                Logger.Warning($"Skipping {Path.GetFileName(osuFile)}: couldn't read it ({e.Message}).");
                continue;
            }

            if (beatmap.GeneralSection.Mode != Ruleset.Mania)
                continue;

            int laneCount = laneCountOf(beatmap);

            if (laneCount < LaneKeyLayouts.MIN_LANES || laneCount > LaneKeyLayouts.MAX_LANES)
                continue;

            difficulties.Add(beatmap);
        }

        return difficulties;
    }

    private static int laneCountOf(OsuBeatmap beatmap) => (int)Math.Round(beatmap.DifficultySection.CircleSize);

    /// <summary>
    /// Mania notes in file order. Holds become real yuuki holds; osu!'s per-map sample sets are dropped
    /// in favour of yuuki's per-type banks (normal = tap, drum = hold), but whistle/finish/clap carry over.
    /// </summary>
    private static BeatmapNote[] convertNotes(OsuBeatmap beatmap, int laneCount)
    {
        var notes = new List<BeatmapNote>();

        foreach (var hitObject in beatmap.HitObjects)
        {
            if (hitObject is not ManiaNote)
                continue;

            bool isHold = hitObject is ManiaHoldNote && hitObject.EndTime > hitObject.StartTime;

            notes.Add(new BeatmapNote
            {
                // Same column mapping as osu!: the x position split evenly across the 512px playfield.
                Lane = (NoteLane)Math.Clamp((int)Math.Floor(hitObject.Position.X * laneCount / 512f), 0, laneCount - 1),
                Type = isHold ? NoteType.Hold : NoteType.Tap,
                StartTime = hitObject.StartTime,
                EndTime = isHold ? hitObject.EndTime : hitObject.StartTime,
                Bank = isHold ? SampleBank.Drum : SampleBank.Normal,
                Additions = additionsOf(hitObject.HitSound)
            });
        }

        return notes.ToArray();
    }

    /// <summary>
    /// The difficulty's red lines as timing points: offset, beat length and meter. Green lines (negative
    /// beat length) are osu!'s slider velocity and volume changes, which yuuki doesn't have.
    /// </summary>
    private static ControlPointInfo timingOf(OsuBeatmap beatmap)
    {
        var timing = new ControlPointInfo();

        foreach (var point in beatmap.TimingPoints.Where(t => t.BeatLength > 0))
            timing.Add(new TimingPoint(point.Offset, point.BeatLength, new YuukiTimeSignature(Math.Max(1, (int)point.TimeSignature))));

        return timing;
    }

    private static HitSampleAddition additionsOf(HitSoundType hitSound)
    {
        var additions = HitSampleAddition.None;

        if (hitSound.HasFlag(HitSoundType.Whistle))
            additions |= HitSampleAddition.Whistle;
        if (hitSound.HasFlag(HitSoundType.Finish))
            additions |= HitSampleAddition.Finish;
        if (hitSound.HasFlag(HitSoundType.Clap))
            additions |= HitSampleAddition.Clap;

        return additions;
    }

    /// <summary>
    /// The BPM that lasts longest, the way osu! picks the BPM it shows: red lines only, the first one
    /// counted from 0, the last one running to the last note.
    /// </summary>
    private static float mostCommonBpm(OsuBeatmap beatmap, double lastNoteTime)
    {
        // Green (inherited) lines carry a negative beat length.
        var timingPoints = beatmap.TimingPoints.Where(t => t.BeatLength > 0).OrderBy(t => t.Offset).ToList();
        if (timingPoints.Count == 0)
            return 0;

        // Grouped on the rounded beat length like osu!, but the exact one is kept: the game times
        // beat-synced things (hold ticks, count-in) from it, where 444.4 ms instead of 444.44 drifts.
        double beatLength = timingPoints
                            .Select((t, i) =>
                            {
                                if (t.Offset > lastNoteTime)
                                    return (beatLength: t.BeatLength, duration: 0d);

                                double start = i == 0 ? 0 : t.Offset;
                                double end = i == timingPoints.Count - 1 ? lastNoteTime : timingPoints[i + 1].Offset;
                                return (beatLength: t.BeatLength, duration: end - start);
                            })
                            .GroupBy(t => Math.Round(t.beatLength, 1))
                            .Select(g => (beatLength: g.First().beatLength, duration: g.Sum(t => t.duration)))
                            .OrderByDescending(t => t.duration)
                            .First().beatLength;

        return (float)(60000 / beatLength);
    }

    private void write(BeatmapSet beatmapSet, List<Beatmap> beatmaps, string audioSource, string? backgroundSource, string? videoSource)
    {
        string folderName = BeatmapSetUtility.GetFolderName(beatmapSet);
        string setFolder = Path.Combine(beatmapsFolder, folderName);

        try
        {
            // Importing the same set again replaces it.
            if (Directory.Exists(setFolder))
                Directory.Delete(setFolder, true);

            Directory.CreateDirectory(setFolder);

            File.Copy(audioSource, Path.Combine(setFolder, beatmapSet.TrackPath));
            if (backgroundSource != null)
                File.Copy(backgroundSource, Path.Combine(setFolder, beatmapSet.BackgroundPath));
            if (videoSource != null)
                File.Copy(videoSource, Path.Combine(setFolder, beatmapSet.VideoPath));

            writeJson(Path.Combine(setFolder, BeatmapSetUtility.GetBeatmapSetFileName(beatmapSet) + ".yks"), beatmapSet);

            foreach (Beatmap beatmap in beatmaps)
                writeJson(Path.Combine(setFolder, BeatmapUtility.GetBeatmapFileName(beatmap) + ".ykb"), beatmap);
        }
        catch
        {
            if (Directory.Exists(setFolder))
                Directory.Delete(setFolder, true);

            throw;
        }

        // The game keeps only the first set it reads for an ID, so an older copy under another name would win at random.
        foreach (string other in Directory.EnumerateDirectories(beatmapsFolder, $"{beatmapSet.ID} *"))
        {
            if (Path.GetFileName(other) != folderName)
                Logger.Warning($"\"{Path.GetFileName(other)}\" has the same set ID ({beatmapSet.ID}) as the import; the game only loads one of them.");
        }
    }

    private static void writeJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, BeatmapUtility.WRITE_OPTIONS));

    /// <summary>
    /// Finds a file the .osu refers to inside the extracted archive. osu! looks files up
    /// case-insensitively, so maps aren't always consistent about case. Never resolves outside the archive.
    /// </summary>
    private static string? findInArchive(string root, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        string rootPath = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        string normalised = relativePath.Trim().Replace('\\', '/');
        string candidate = Path.GetFullPath(Path.Combine(rootPath, normalised));

        if (!candidate.StartsWith(rootPath, StringComparison.Ordinal))
            return null;

        if (File.Exists(candidate))
            return candidate;

        return Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                        .FirstOrDefault(f => string.Equals(Path.GetRelativePath(rootPath, f).Replace('\\', '/'), normalised, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The set ID from an osu!-style "123456 Artist - Title.osz" file name.</summary>
    private static int? idFromOszName(string oszName)
    {
        string digits = new string(oszName.TakeWhile(char.IsAsciiDigit).ToArray());
        return digits.Length > 0 && digits.Length < oszName.Length && oszName[digits.Length] == ' ' && int.TryParse(digits, out int id) && id > 0 ? id : null;
    }

    /// <summary>
    /// A stable ID for unsubmitted maps, so importing the same map again keeps its scores. Kept above
    /// 1,000,000,000 so it never lands on a real osu! ID.
    /// </summary>
    private static int generatedId(params string?[] parts)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', parts)));
        return 1_000_000_000 + (int)(BitConverter.ToUInt32(hash) % 1_000_000_000);
    }

    private static string firstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
}

/// <summary>
/// What became of one .osz.
/// </summary>
public readonly record struct OszImportResult(bool Success, BeatmapSet? BeatmapSet, int DifficultyCount, string? Error)
{
    public static OszImportResult Imported(BeatmapSet set, int difficulties) => new OszImportResult(true, set, difficulties, null);

    public static OszImportResult Failed(string error) => new OszImportResult(false, null, 0, error);
}
