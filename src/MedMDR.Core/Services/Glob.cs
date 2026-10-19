using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace MedMDR.Core;

/// <summary>Comodines estilo Windows: * y ? (sin distinguir mayúsculas).</summary>
public static class Glob
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new();

    public static bool IsMatch(string name, string pattern)
    {
        var re = Cache.GetOrAdd(pattern, p =>
            new Regex("^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));
        return re.IsMatch(name);
    }

    public static bool MatchesAny(string path, IEnumerable<string> patterns)
    {
        string name = path.Replace('\\', '/').Split('/').Last();
        foreach (var p in patterns)
        {
            var pat = p.Trim();
            if (pat.Length == 0) continue;
            if (IsMatch(name, pat) || IsMatch(path.Replace('\\', '/'), pat)) return true;
        }
        return false;
    }
}
