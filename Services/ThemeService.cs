using System;
using System.Windows;
using System.Windows.Media;

namespace TaskCore.Services
{
    /// <summary>
    /// Service for managing application theme and accent colors at runtime.
    /// </summary>
    public static class ThemeService
    {
        // Predefined accent colors
        public static readonly string[] PresetColors = new[]
        {
            "#FF9A4DFF", // Purple (default)
            "#FF00D4AA", // Cyan/Teal
            "#FFFF4D9A", // Pink
            "#FFFF4D4D", // Red
            "#FF4DFF4D", // Green
            "#FFFFA500", // Orange
            "#FF4D9AFF", // Blue
            "#FFFFD700", // Gold
        };

        public static readonly string[] PresetColorNames = new[]
        {
            "Purple",
            "Cyan",
            "Pink",
            "Red",
            "Green",
            "Orange",
            "Blue",
            "Gold"
        };

        /// <summary>
        /// Applies the accent color to the application resources.
        /// </summary>
        public static void ApplyAccentColor(string hexColor)
        {
            if (!TryParseColor(hexColor, out var color))
            {
                // Fallback to default purple if invalid
                TryParseColor("#FF9A4DFF", out color);
            }

            var accentBrush = new SolidColorBrush(color);
            accentBrush.Freeze();

            // Calculate hover (lighter) and pressed (darker) variants
            var hoverColor = LightenColor(color, 0.15);
            var pressedColor = DarkenColor(color, 0.15);

            var hoverBrush = new SolidColorBrush(hoverColor);
            var pressedBrush = new SolidColorBrush(pressedColor);
            hoverBrush.Freeze();
            pressedBrush.Freeze();

            // Update application resources
            Application.Current.Resources["AccentBrush"] = accentBrush;
            Application.Current.Resources["AccentHoverBrush"] = hoverBrush;
            Application.Current.Resources["AccentPressedBrush"] = pressedBrush;

            // Update the Color resources as well for dynamic resource binding
            Application.Current.Resources["AccentColor"] = color;
            Application.Current.Resources["AccentHoverColor"] = hoverColor;
            Application.Current.Resources["AccentPressedColor"] = pressedColor;
        }

        /// <summary>
        /// Validates and normalizes a hex color string.
        /// </summary>
        public static bool TryParseColor(string hexColor, out Color color)
        {
            color = Colors.Purple;

            if (string.IsNullOrWhiteSpace(hexColor))
                return false;

            hexColor = hexColor.Trim();

            // Handle # prefix
            if (hexColor.StartsWith("#"))
                hexColor = hexColor.Substring(1);

            // Handle 3-digit hex (RGB)
            if (hexColor.Length == 3)
            {
                hexColor = $"{hexColor[0]}{hexColor[0]}{hexColor[1]}{hexColor[1]}{hexColor[2]}{hexColor[2]}FF";
            }
            // Handle 6-digit hex (RRGGBB)
            else if (hexColor.Length == 6)
            {
                hexColor = hexColor + "FF";
            }
            // Handle 8-digit hex (AARRGGBB)
            else if (hexColor.Length != 8)
            {
                return false;
            }

            try
            {
                byte a = Convert.ToByte(hexColor.Substring(0, 2), 16);
                byte r = Convert.ToByte(hexColor.Substring(2, 2), 16);
                byte g = Convert.ToByte(hexColor.Substring(4, 2), 16);
                byte b = Convert.ToByte(hexColor.Substring(6, 2), 16);

                color = Color.FromArgb(a, r, g, b);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Lightens a color by a specified factor (0-1).
        /// </summary>
        private static Color LightenColor(Color color, double factor)
        {
            factor = Math.Clamp(factor, 0, 1);

            byte r = (byte)Math.Min(255, color.R + (255 - color.R) * factor);
            byte g = (byte)Math.Min(255, color.G + (255 - color.G) * factor);
            byte b = (byte)Math.Min(255, color.B + (255 - color.B) * factor);

            return Color.FromArgb(color.A, r, g, b);
        }

        /// <summary>
        /// Darkens a color by a specified factor (0-1).
        /// </summary>
        private static Color DarkenColor(Color color, double factor)
        {
            factor = Math.Clamp(factor, 0, 1);

            byte r = (byte)(color.R * (1 - factor));
            byte g = (byte)(color.G * (1 - factor));
            byte b = (byte)(color.B * (1 - factor));

            return Color.FromArgb(color.A, r, g, b);
        }

        /// <summary>
        /// Converts a Color to hex string format.
        /// </summary>
        public static string ToHexString(Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }
}
