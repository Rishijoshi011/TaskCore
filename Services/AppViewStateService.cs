using System;

namespace TaskCore.Services
{
    public static class AppViewStateService
    {
        private static readonly object _lock = new();
        private static string _currentView = string.Empty;

        public static event Action<string>? ViewChanged;

        public static string CurrentView
        {
            get
            {
                lock (_lock) return _currentView;
            }
        }

        public static void SetView(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName)) return;

            Action<string>? handler;
            lock (_lock)
            {
                if (_currentView == viewName) return;
                _currentView = viewName;
                handler = ViewChanged;
            }

            handler?.Invoke(viewName);
        }
    }
}
