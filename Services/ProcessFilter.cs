using System.Diagnostics;

namespace TaskCore.Services
{
    internal static class ProcessFilter
    {
        public static bool IsSystemAppName(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName)) return false;

            var name = appName.ToLowerInvariant();

            // Core Windows system processes
            if (name == "system" ||
                name == "idle" ||
                name == "registry" ||
                name.StartsWith("svchost") ||
                name.StartsWith("csrss") ||
                name.StartsWith("wininit") ||
                name.StartsWith("smss") ||
                name.StartsWith("dwm") ||
                name.StartsWith("lsass") ||
                name.StartsWith("lsm") ||
                name.StartsWith("services") ||
                name.StartsWith("winlogon") ||
                name.StartsWith("conhost") ||
                name.StartsWith("sihost") ||
                name.StartsWith("taskhost") ||
                name.StartsWith("runtimebroker") ||
                name.StartsWith("searchindexer") ||
                name.StartsWith("fontdrvhost") ||
                name.StartsWith("spoolsv") ||
                name.StartsWith("wmi") ||
                name.StartsWith("wmiprvse") ||
                name.StartsWith("ctfmon") ||
                name.StartsWith("secure system") ||
                name.StartsWith("memory compression") ||
                name.StartsWith("audiodg"))
                return true;

            // Services ending with svc (case insensitive)
            if (name.EndsWith("svc") || name.EndsWith("service") || name.EndsWith("serv") ||
                name.Contains("svc") || name.Contains("SVC"))
                return true;

            // Windows Update and core worker processes
            if (name.Contains("usocore") || name.Contains("uso") || name.Contains("worker") ||
                name.Contains("trustedinstaller") || name.Contains("tiworker"))
                return true;

            // More Windows core services
            if (name.StartsWith("core") || name.StartsWith("smsvchost") ||
                name.Contains("clr") || name.Contains("mscorsvw") || name.Contains("ngen"))
                return true;

            // Security/Auth services
            if (name.Contains("crypto") || name.Contains("vault") || name.Contains("keyiso") ||
                name.Contains("samss") || name.Contains("ntlm") || name.Contains("kerberos"))
                return true;

            // Network related services
            if (name.Contains("network") || name.Contains("net") || name.Contains("tcp") ||
                name.Contains("ip") || name.Contains("dhcp") || name.Contains("dns") ||
                name.Contains("proxy") || name.Contains("tunnel"))
                return true;

            // More host processes
            if (name.Contains("svchost") || name.Contains("taskhost") || name.Contains("rdp") ||
                name.Contains("termsrv") || name.Contains("winlogon"))
                return true;

            // WMI and instrumentation
            if (name.Contains("wmi") || name.Contains("wmiprvse") || name.Contains("wbem"))
                return true;

            // Graphics/display drivers
            if (name.StartsWith("igfx") || name.Contains("display") || name.Contains("gpu"))
                return true;

            // Root/embedded processes
            if (name.Contains("root") || name.Contains("embedded"))
                return true;

            // Development tools and IDE-related
            if (name.Contains("devenv") ||
                name.Contains("copilot") ||
                name.Contains("language_server") ||
                name.Contains("servicehub") ||
                name.StartsWith("msbuild") ||
                name.Contains("vbcsccompiler") ||
                name.Contains("perfwatson") ||
                name.Contains("vbcscompiler") ||
                name.Contains("openconsole") ||
                name.Contains("livepreviewsurface") ||
                name.Contains("sapiservice") ||
                name.Contains("snippingtool") ||
                name.Contains("sqlceip") ||
                name.Contains("sqlwriter") ||
                name.Contains("sqlservr") ||
                name.StartsWith("dotnet") ||
                name.Contains("omnisharp") ||
                name.Contains("browserlink") ||
                name.StartsWith("windsurf") ||
                name.Contains("taskcore"))
                return true;

            // Edge/Chrome helpers and WebView
            if (name.Contains("webview2") ||
                name.Contains("crashpad") ||
                name.Contains("crashhandler") ||
                name.Contains("bravecrash") ||
                name.EndsWith("helper") ||
                name.Contains("renderer"))
                return true;

            // Microsoft background services (expanded)
            if (name.Contains("onlinent") ||
                name.Contains("oninit") ||
                name.Contains("update") ||
                name.Contains("install") ||
                name.Contains("agent") ||
                name.Contains("broker") ||
                name.Contains("daemon") ||
                name.Contains("host") ||
                name.Contains("widget") ||
                name.Contains("experience") ||
                name.Contains("lsalso") ||
                name.Contains("sihost"))
                return true;

            // Antivirus/Security services
            if (name.Contains("av") ||
                name.Contains("sec") ||
                name.Contains("protect") ||
                name.Contains("guard") ||
                name.Contains("shield") ||
                name.Contains("scan") ||
                name.Contains("defender") ||
                name.Contains("firewall") ||
                name.Contains("qh") ||
                name.Contains("mcafee") ||
                name.Contains("norton") ||
                name.Contains("kaspersky") ||
                name.Contains("avast") ||
                name.Contains("avg") ||
                name.Contains("bitdefender") ||
                name.Contains("eset") ||
                name.Contains("fsecure") ||
                name.Contains("sophos") ||
                name.Contains("trend") ||
                name.Contains("symantec") ||
                name.Contains("malwarebytes"))
                return true;

            // Database and cache services
            if (name.Contains("sql") ||
                name.Contains("redis") ||
                name.Contains("mongo") ||
                name.Contains("postgres") ||
                name.Contains("mysql") ||
                name.Contains("mssql"))
                return true;

            // Background services and utilities (aggressive filtering)
            if (name.EndsWith("nt") ||
                name.Contains("serv") ||
                name.Contains("hub") ||
                name.Contains("control") ||
                name.Contains("wait") ||
                name.Contains("dialog") ||
                name.Contains("detour") ||
                name.Contains("identity") ||
                name.Contains("intelli") ||
                name.Contains("roslyn") ||
                name.Contains("index") ||
                name.Contains("datawarehouse") ||
                name.Contains("warehouse") ||
                name.Contains("anydesk") ||
                name.Contains("onedrive") ||
                name.Contains("update") ||
                name.Contains("sync"))
                return true;

            return false;
        }

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
                    if (p.MainModule?.FileName?.StartsWith(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase) == true)
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
    }
}
