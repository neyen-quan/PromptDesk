using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;

namespace PromptDesk
{
    internal static class ModelPalette
    {
        internal static readonly KeyValuePair<string, Color>[] Presets = {
            new KeyValuePair<string, Color>("Azul", Color.FromArgb(112, 172, 255)),
            new KeyValuePair<string, Color>("Amarillo", Color.FromArgb(245, 199, 85)),
            new KeyValuePair<string, Color>("Violeta", Color.FromArgb(181, 151, 255)),
            new KeyValuePair<string, Color>("Verde", Color.FromArgb(104, 213, 183)),
            new KeyValuePair<string, Color>("Coral", Color.FromArgb(242, 143, 155)),
            new KeyValuePair<string, Color>("Naranja", Color.FromArgb(244, 168, 117)),
            new KeyValuePair<string, Color>("Rosa", Color.FromArgb(217, 147, 220)),
            new KeyValuePair<string, Color>("Turquesa", Color.FromArgb(125, 204, 216))
        };

        internal static Color For(Store store, string model)
        {
            if (String.IsNullOrWhiteSpace(model)) return Color.Empty;
            string hex;
            if (store != null && store.ModelColors.TryGetValue(model, out hex))
                return ColorTranslator.FromHtml(hex);
            return Automatic(model);
        }

        internal static Color Automatic(string model)
        {
            if (String.IsNullOrWhiteSpace(model)) return Color.Empty;
            string name = model.Trim().ToLowerInvariant();
            if (Regex.IsMatch(name, @"\bgpt[\s_\-]*personal\b")) return Presets[0].Value;
            if (Regex.IsMatch(name, @"\b(?:(?:nano[\s_\-]*)?banana|seedream|seedance)(?=\d|\b)")) return Presets[1].Value;
            if (Regex.IsMatch(name, @"\b(?:wan|one)(?=\d|\b)")) return Presets[2].Value;

            // String.GetHashCode can change across processes or runtimes.
            uint hash = 2166136261u;
            foreach (char letter in name) hash = unchecked((hash ^ letter) * 16777619u);
            const int firstSecondary = 3;
            int offset = (int)(hash % (uint)(Presets.Length - firstSecondary));
            return Presets[firstSecondary + offset].Value;
        }

        internal static Color Ink(Color accent)
        {
            // Even custom dark colors need a legible stroke and label against the dark card.
            int luminance = (299 * accent.R + 587 * accent.G + 114 * accent.B) / 1000;
            return luminance >= 150 ? accent : Blend(accent, Color.White, (150 - luminance) * 100 / (255 - luminance));
        }

        internal static Color Surface(Color accent, bool selected)
        {
            return Blend(UI.Input, Ink(accent), selected ? 24 : 16);
        }

        internal static Color Blend(Color background, Color foreground, int percent)
        {
            return Color.FromArgb((background.R * (100 - percent) + foreground.R * percent) / 100,
                (background.G * (100 - percent) + foreground.G * percent) / 100,
                (background.B * (100 - percent) + foreground.B * percent) / 100);
        }
    }
}
