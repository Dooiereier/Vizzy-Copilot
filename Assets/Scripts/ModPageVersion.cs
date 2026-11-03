namespace Assets.Scripts
{
    using System;
    using System.Text.RegularExpressions;

    /// <summary>Reads the "Mod Version" from a SimpleRockets.com mod page. Plain text logic, tested offline.</summary>
    public static class ModPageVersion
    {
        // The page's "General info" box: <span class="spec-label">Mod Version:</span> then
        // <span class="spec-value">1.0</span>.
        private static readonly Regex PageVersion = new Regex(
            @"Mod Version:\s*</span>\s*<span[^>]*class=""spec-value""[^>]*>\s*(\d+(?:\.\d+){1,3})\s*<",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>The version on the page, or null if it shows none.</summary>
        public static Version Parse(string html)
        {
            var match = PageVersion.Match(html ?? string.Empty);
            return match.Success && Version.TryParse(match.Groups[1].Value, out var version) ? version : null;
        }
    }
}
