using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;

namespace TaskCore.Services
{
    public static class IconService
    {
        private static readonly Dictionary<string, BitmapSource?> _iconCache = new();
        private static readonly LinkedList<string> _cacheAccessOrder = new();
        private const int MaxCacheSize = 200;
        private static BitmapSource? _defaultIcon;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ExtractAssociatedIcon(IntPtr hInst, string lpIconPath, out ushort lpiIcon);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        public static BitmapSource? GetIconForPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return GetDefaultIcon();

            // Check cache first
            lock (_iconCache)
            {
                if (_iconCache.TryGetValue(path, out var cached))
                {
                    // Move to front (most recently used)
                    _cacheAccessOrder.Remove(path);
                    _cacheAccessOrder.AddFirst(path);
                    return cached ?? GetDefaultIcon();
                }
            }

            try
            {
                BitmapSource? icon = null;

                // Try multiple methods to get icon
                if (File.Exists(path))
                {
                    // Method 1: SHGetFileInfo for better quality
                    icon = ExtractIconWithSHGetFileInfo(path, largeIcon: true);

                    // Method 2: Fallback to ExtractAssociatedIcon
                    if (icon == null)
                        icon = ExtractIcon(path);
                }
                else
                {
                    // For processes where path isn't accessible, try using file attributes
                    icon = ExtractIconWithSHGetFileInfo(path, largeIcon: true, useFileAttributes: true);
                }

                // Cache result (even if null)
                lock (_iconCache)
                {
                    // Evict oldest if at capacity
                    if (_iconCache.Count >= MaxCacheSize && _cacheAccessOrder.Count > 0)
                    {
                        var oldest = _cacheAccessOrder.Last?.Value;
                        if (oldest != null)
                        {
                            _iconCache.Remove(oldest);
                            _cacheAccessOrder.RemoveLast();
                        }
                    }

                    _iconCache[path] = icon;
                    _cacheAccessOrder.AddFirst(path);
                }
                return icon ?? GetDefaultIcon();
            }
            catch
            {
                lock (_iconCache)
                {
                    _iconCache[path] = null;
                    _cacheAccessOrder.AddFirst(path);
                }
                return GetDefaultIcon();
            }
        }

        private static BitmapSource? ExtractIconWithSHGetFileInfo(string path, bool largeIcon, bool useFileAttributes = false)
        {
            var shinfo = new SHFILEINFO();
            var flags = SHGFI_ICON | (largeIcon ? SHGFI_LARGEICON : SHGFI_SMALLICON);

            if (useFileAttributes)
                flags |= SHGFI_USEFILEATTRIBUTES;

            var fileAttr = useFileAttributes ? FILE_ATTRIBUTE_NORMAL : 0;

            try
            {
                var result = SHGetFileInfo(path, fileAttr, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
                if (result == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
                    return null;

                try
                {
                    return IconToBitmapSource(shinfo.hIcon);
                }
                finally
                {
                    DestroyIcon(shinfo.hIcon);
                }
            }
            catch
            {
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
                return IconToBitmapSource(hIcon);
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        private static BitmapSource IconToBitmapSource(IntPtr hIcon)
        {
            using var icon = Icon.FromHandle(hIcon);
            using var bitmap = icon.ToBitmap();
            IntPtr hBitmap = IntPtr.Zero;

            try
            {
                hBitmap = bitmap.GetHbitmap();
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                bitmapSource.Freeze();
                return bitmapSource;
            }
            finally
            {
                // Clean up GDI object to prevent memory leak
                if (hBitmap != IntPtr.Zero)
                    DeleteObject(hBitmap);
            }
        }

        public static BitmapSource? GetDefaultIcon()
        {
            if (_defaultIcon != null)
                return _defaultIcon;

            try
            {
                // Use system application icon as default
                var shinfo = new SHFILEINFO();
                var flags = SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES;
                var result = SHGetFileInfo(".exe", FILE_ATTRIBUTE_NORMAL, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);

                if (result != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        _defaultIcon = IconToBitmapSource(shinfo.hIcon);
                        return _defaultIcon;
                    }
                    finally
                    {
                        DestroyIcon(shinfo.hIcon);
                    }
                }
            }
            catch { }

            return null;
        }

        public static void ClearCache()
        {
            lock (_iconCache)
            {
                _iconCache.Clear();
                _cacheAccessOrder.Clear();
            }
        }
    }
}
