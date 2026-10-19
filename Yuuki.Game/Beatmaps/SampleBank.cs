// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// The sample set a note's hitsounds are drawn from. Each bank has
/// its own <c>hitnormal</c>/<c>hitwhistle</c>/<c>hitfinish</c>/<c>hitclap</c> and slider samples
/// under <c>Samples/Gameplay/</c> (e.g. <c>normal-hitnormal.wav</c>, <c>drum-sliderslide.wav</c>).
/// </summary>
public enum SampleBank
{
    /// <summary>
    /// The default, punchy set.
    /// </summary>
    Normal,

    /// <summary>
    /// A quieter, softer set.
    /// </summary>
    Soft,

    /// <summary>
    /// A percussive drum set.
    /// </summary>
    Drum
}

public static class SampleBankExtensions
{
    /// <summary>
    /// The filename prefix for this bank (e.g. <c>normal</c>), matching the sample files.
    /// </summary>
    public static string Prefix(this SampleBank bank) => bank switch
    {
        SampleBank.Soft => "soft",
        SampleBank.Drum => "drum",
        _ => "normal"
    };
}
