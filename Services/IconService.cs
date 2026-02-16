using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;

namespace TaskCore.Services
{
    public static class IconService
    {
        private static readonly Dictionary<string, BitmapSource?> _iconCache = new();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ExtractAssociatedIcon(IntPtr hInst, string lpIconPath, out ushort lpiIcon);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public static BitmapSource? GetIconForPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            // Check cache
            if (_iconCache.TryGetValue(path, out var cached))
                return cached;

            try
            {
                var icon = ExtractIcon(path);
                _iconCache[path] = icon;
                return icon;
            }
            catch
            {
                _iconCache[path] = null;
                return null;
            }
        }

        private static BitmapSource? ExtractIcon(string path)
        {
            var hIcon = ExtractAssociatedIcon(IntPtr.Zero, path, out _);
            if (hIcon == IntPtr.Zero)
                return null;

            try
            {
                var icon = System.Drawing.Icon.FromHandle(hIcon);
                var bitmap = icon.ToBitmap();

                // Convert to BitmapSource
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap.GetHbitmap(),
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                bitmapSource.Freeze();
                return bitmapSource;
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        public static BitmapSource? GetDefaultIcon()
        {
            // Return a simple default icon (can be improved later)
            return null;
        }
    }
}
