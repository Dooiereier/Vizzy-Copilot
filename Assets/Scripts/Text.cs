namespace Assets.Scripts
{
    using System;
    using System.Globalization;

    /// <summary>
    /// The mod's text for players, from its language files (Assets/Content/Languages) through the game's
    /// Locale, so it follows the game's language and falls back to English. Keys start with "VizzyStudio.".
    /// The offline tests replace the lookup with the English file.
    /// </summary>
    public static class Text
    {
        public static Func<string, string> Resolve { get; set; } = key => ModApi.Locale.GetString(key);

        public static string Get(string key)
        {
            return Resolve(key);
        }

        /// <summary>With {0}, {1}… filled in, so each language can put them where its grammar needs.</summary>
        public static string Get(string key, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, Resolve(key), args);
        }

        /// <summary>The "One" text for 1, the "Many" text otherwise, with the count as {0}.</summary>
        public static string Count(string key, int count, params object[] args)
        {
            var all = new object[args.Length + 1];
            all[0] = count;
            Array.Copy(args, 0, all, 1, args.Length);
            return Get(key + (count == 1 ? ".One" : ".Many"), all);
        }
    }
}
