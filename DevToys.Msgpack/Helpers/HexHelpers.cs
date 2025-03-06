using System.Text.RegularExpressions;

namespace DevToys.Msgpack.Helpers;

public static partial class HexHelpers
{
    // Replace dashes, commas, and any whitespaces
    [GeneratedRegex("[-,\\s]+")]
    public static partial Regex CleanHexRegex();
}

