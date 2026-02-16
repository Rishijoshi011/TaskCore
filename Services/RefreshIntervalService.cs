using System;
using System.Windows;

namespace TaskCore.Services
{
    public static class RefreshIntervalService
    {
        private static readonly object _lock = new();
        private static int _currentMs = 1000;

        public static event Action<int>? RefreshIntervalChanged;

        public static int CurrentMs
        {
            get
            {
                lock (_lock) return _currentMs;
            }
        }

        public static int Normalize(int ms)
        {
            if (ms < 500) ms = 500;
            if (ms > 10000) ms = 10000;
            return ms;
        }

        public static void Set(int ms)
        {
            ms = Normalize(ms);

            Action<int>? handler;
            lock (_lock)
            {
                if (_currentMs == ms) return;
                _currentMs = ms;
                handler = RefreshIntervalChanged;
            }

            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    try { handler?.Invoke(ms); } catch { }
                });
            }
            catch
            {
                try { handler?.Invoke(ms); } catch { }
            }
        }
    }
}
