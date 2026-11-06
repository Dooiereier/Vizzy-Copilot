using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

/// <summary>The English language file as the text lookup for the offline tests, and checks on the language files.</summary>
public static class EnglishText
{
    private static string Folder => Path.Combine(Environment.GetEnvironmentVariable("VIZZY_STUDIO_ROOT") ?? ".", "Assets", "Content", "Languages");

    public static Func<string, string> Load()
    {
        var strings = Read("EN-US");
        return key => strings.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>As the game's Locale reads it: a literal \\n becomes a line break.</summary>
    private static Dictionary<string, string> Read(string language)
    {
        var path = Path.Combine(Folder, language + ".xml");
        return XDocument.Load(path).Root.Elements("s").ToDictionary(item => (string)item.Attribute("id"), item => item.Value.Replace("\\n", "\n"));
    }

    public static void Run(Action<string, bool> check)
    {
        var english = Read("EN-US");
        // Every key the code names is in the English file: a missing one would show players the raw key.
        var sources = Path.Combine(Environment.GetEnvironmentVariable("VIZZY_STUDIO_ROOT") ?? ".", "Assets", "Scripts");
        var missing = new List<string>();
        foreach (var file in Directory.GetFiles(sources, "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "\"(\\{?)(VizzyStudio\\.[A-Za-z]+\\.[A-Za-z.]*[A-Za-z])\\}?\""))
            {
                var key = match.Groups[2].Value;
                if (!english.ContainsKey(key) && !(english.ContainsKey(key + ".One") && english.ContainsKey(key + ".Many")))
                {
                    missing.Add(Path.GetFileName(file) + ": " + key);
                }
            }
        }

        check("every key the code names is in the English file" + (missing.Count > 0 ? " (missing " + string.Join(", ", missing.Distinct()) + ")" : string.Empty), missing.Count == 0);

        foreach (var language in new[] { "pt", "zh-Hans" })
        {
            if (!File.Exists(Path.Combine(Folder, language + ".xml")))
            {
                check("language file " + language + " exists", false);
                continue;
            }

            var other = Read(language);
            check(language + ": every English key is translated", english.Keys.All(other.ContainsKey));
            check(language + ": no key English doesn't have", other.Keys.All(english.ContainsKey));
            check(language + ": no empty text", other.Values.All(value => value.Trim().Length > 0));
            check(language + ": the same {n} placeholders", english.All(pair => !other.ContainsKey(pair.Key) || Placeholders(pair.Value) == Placeholders(other[pair.Key])));
        }
    }

    private static string Placeholders(string text)
    {
        return string.Join(",", Regex.Matches(text, @"\{\d+\}").Cast<Match>().Select(match => match.Value).Distinct().OrderBy(value => value));
    }
}
