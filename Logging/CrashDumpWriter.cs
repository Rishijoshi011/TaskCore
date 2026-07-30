using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TaskCore.Logging
{
    public static class CrashDumpWriter
    {
        [Flags]
        public enum MiniDumpType : uint
        {
            Normal = 0x00000000,
            WithDataSegs = 0x00000001,
            WithFullMemory = 0x00000002,
            WithHandleData = 0x00000004,
            WithThreadInfo = 0x00001000
        }

        [DllImport("dbghelp.dll", EntryPoint = "MiniDumpWriteDump", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool MiniDumpWriteDump(
            IntPtr hProcess,
            uint processId,
            SafeHandle hFile,
            uint dumpType,
            IntPtr exceptionParam,
            IntPtr userStreamParam,
            IntPtr callbackParam);

        public static string? CreateCrashDump()
        {
            try
            {
                var dumpDir = Path.Combine(SessionManager.GetLogsDirectory(), "CrashDumps");
                Directory.CreateDirectory(dumpDir);

                var fileName = $"Crash_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_PID{Environment.ProcessId}.dmp";
                var dumpPath = Path.Combine(dumpDir, fileName);

                using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                using var stream = new FileStream(dumpPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var success = MiniDumpWriteDump(
                    currentProcess.Handle,
                    (uint)currentProcess.Id,
                    stream.SafeFileHandle,
                    (uint)(MiniDumpType.Normal | MiniDumpType.WithThreadInfo | MiniDumpType.WithHandleData),
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);

                return success ? dumpPath : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
