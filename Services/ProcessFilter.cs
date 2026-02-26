using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TaskCore.Services
{
    /// <summary>
    /// Filters for identifying Windows system processes and background services.
    /// Uses HashSet-based lookups for O(1) performance instead of repeated string scanning.
    /// </summary>
    internal static class ProcessFilter
    {
        #region Static Pattern Sets (initialized once for performance)

        // Core Windows system processes - critical OS components
        private static readonly HashSet<string> CoreWindowsProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "registry",
            "svchost", "csrss", "wininit", "smss", "dwm",
            "lsass", "lsm", "services", "winlogon",
            "conhost", "sihost", "taskhost", "runtimebroker",
            "searchindexer", "fontdrvhost", "spoolsv",
            "wmi", "wmiprvse", "ctfmon",
            "audiodg", "smsvchost"
        };

        // Windows core services - background infrastructure
        private static readonly string[] WindowsServicePatterns =
        {
            "core", "clr", "mscorsvw", "ngen",
            "crypto", "vault", "keyiso", "samss", "ntlm", "kerberos",
            "usocore", "uso", "trustedinstaller", "tiworker"
        };

        // Network-related services
        private static readonly string[] NetworkPatterns =
        {
            "network", "dhcp", "dns", "proxy", "tunnel"
        };

        // Development tools and IDE-related processes
        private static readonly HashSet<string> DevTools = new(StringComparer.OrdinalIgnoreCase)
        {
            "devenv", "copilot", "language_server", "servicehub",
            "msbuild", "vbcsccompiler", "perfwatson", "vbcscompiler",
            "openconsole", "livepreviewsurface", "sapiservice",
            "snippingtool", "sqlceip", "sqlwriter", "sqlservr",
            "dotnet", "omnisharp", "browserlink", "windsurf", "taskcore"
        };

        // Browser/WebView helper processes
        private static readonly string[] BrowserHelpers =
        {
            "webview2", "crashpad", "crashhandler", "bravecrash", "renderer"
        };

        // Microsoft background services
        private static readonly string[] MsBackgroundServices =
        {
            "onlinent", "oninit", "widget", "experience", "lsalso"
        };

        // Antivirus/Security products (specific names)
        private static readonly HashSet<string> SecurityProducts = new(StringComparer.OrdinalIgnoreCase)
        {
            "mcafee", "norton", "kaspersky", "avast", "avg",
            "bitdefender", "eset", "fsecure", "sophos", "trend",
            "symantec", "malwarebytes", "quickheal", "qh", "defender"
        };

        // Database services
        private static readonly string[] DatabasePatterns =
        {
            "redis", "mongo", "postgres", "mysql", "mssql"
        };

        // Background utilities (aggressive filtering)
        private static readonly string[] BackgroundPatterns =
        {
            "roslyn", "datawarehouse", "warehouse", "anydesk", "onedrive", "sync"
        };

        #endregion

        /// <summary>
        /// Determines if an app name represents a system/background process
        /// that should be filtered from user-facing displays.
        /// </summary>
        public static bool IsSystemAppName(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName)) return false;

            var name = appName.ToLowerInvariant();

            // 1. Direct exact matches (O(1) HashSet lookup)
            if (CoreWindowsProcesses.Contains(name))
                return true;

            // 2. Check for services (by suffix or embedded)
            if (name.EndsWith("svc") || name.EndsWith("service") ||
                name.EndsWith("serv") || name.EndsWith("nt") ||
                name.Contains("svc") || name.Contains("service"))
                return true;

            // 3. Check common Windows prefixes
            foreach (var prefix in new[] { "svchost", "csrss", "wininit", "smsvchost", "dwm", "lsass", "lsm" })
            {
                if (name.StartsWith(prefix))
                    return true;
            }

            // 4. Windows infrastructure patterns
            foreach (var pattern in WindowsServicePatterns)
            {
                if (name.Contains(pattern))
                    return true;
            }

            // 5. Network services
            foreach (var pattern in NetworkPatterns)
            {
                if (name.Contains(pattern))
                    return true;
            }

            // 6. WMI and instrumentation
            if (name.Contains("wmi") || name.Contains("wbem") || 
                name.Contains("termsrv") || name.Contains("rdp"))
                return true;

            // 7. Graphics/display drivers
            if (name.StartsWith("igfx") || name.Contains("gpu"))
                return true;

            // 8. Root/embedded
            if (name.Contains("root") || name.Contains("embedded"))
                return true;

            // 9. Development tools (HashSet lookup)
            if (DevTools.Contains(name))
                return true;

            // 10. Browser helpers
            foreach (var pattern in BrowserHelpers)
            {
                if (name.Contains(pattern))
                    return true;
            }

            // 11. Microsoft background services
            foreach (var pattern in MsBackgroundServices)
            {
                if (name.Contains(pattern))
                    return true;
            }

            // 12. Security products (HashSet for exact matches, Contains for patterns)
            if (SecurityProducts.Contains(name) ||
                name.Contains("av") || name.Contains("sec") ||
                name.Contains("protect") || name.Contains("guard") ||
                name.Contains("shield") || name.Contains("scan") ||
                name.Contains("firewall"))
                return true;

            // 13. SQL Server / databases
            if (name.StartsWith("sql") || name == "sqlservr")
            {
                foreach (var db in DatabasePatterns)
                {
                    if (name.Contains(db))
                        return true;
                }
            }

            // 14. Background utilities
            foreach (var pattern in BackgroundPatterns)
            {
                if (name.Contains(pattern))
                    return true;
            }

            // 15. Common background service indicators
            if (name.Contains("hub") || name.Contains("control") ||
                name.Contains("wait") || name.Contains("dialog") ||
                name.Contains("detour") || name.Contains("identity") ||
                name.Contains("intelli") || name.Contains("index"))
                return true;

            return false;
        }

        /// <summary>
        /// Determines if a Process object represents a core Windows system process.
        /// This is more conservative than IsSystemAppName and checks actual process properties.
        /// </summary>
        public static bool IsSystemProcess(Process p)
        {
            try
            {
                var name = p.ProcessName.ToLowerInvariant();

                // Core Windows system processes only
                if (name == "system" ||
                    name == "idle" ||
                    name.StartsWith("svchost") ||
                    name.StartsWith("csrss") ||
                    name.StartsWith("wininit") ||
                    name.StartsWith("smss") ||
                    name.StartsWith("dwm") ||
                    name.StartsWith("lsass") ||
                    name.StartsWith("lsm"))
                    return true;

                // Skip processes from Windows directory
                try
                {
                    var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    var exePath = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath) &&
                        exePath.StartsWith(windowsPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch
                {
                    return false;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if a process is a development tool (IDE, compiler, etc.)
        /// </summary>
        public static bool IsDevTool(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName)) return false;
            return DevTools.Contains(appName.ToLowerInvariant());
        }

        /// <summary>
        /// Check if a process matches user-defined custom filter patterns
        /// </summary>
        public static bool IsUserFilteredProcess(string appName)
        {
            var settings = AppSettingsService.Load();
            return IsUserFilteredProcess(appName, settings);
        }

        /// <summary>
        /// Check if a process matches user-defined custom filter patterns (with cached settings)
        /// </summary>
        public static bool IsUserFilteredProcess(string appName, AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(appName)) return false;
            if (!settings.EnableCustomProcessFilters || settings.CustomFilteredProcesses == null)
                return false;

            var name = appName.ToLowerInvariant();
            foreach (var pattern in settings.CustomFilteredProcesses)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                
                var lowerPattern = pattern.ToLowerInvariant();
                if (name == lowerPattern || name.Contains(lowerPattern))
                    return true;
            }

            return false;
        }
    }
}
