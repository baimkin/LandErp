// Loaded by Windows PowerShell 5.1 Add-Type. Keep this deployment helper C# 5 compatible.
// It owns only child-process console streams and closed technical log files, never ERP data.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LandErp.Production
{
    public sealed class DailyLog : IDisposable
    {
        private readonly object sync = new object();
        private readonly string root, service, channel, session;
        private readonly int maxBytes;
        private FileStream file;
        private string day = "";
        private int sequence;
        public string CurrentPath { get; private set; }

        public DailyLog(string root, string service, string channel, string session, int maxBytes)
        {
            if (service != "server" && service != "worker") throw new ArgumentException("Invalid service.");
            if (channel != "out" && channel != "err") throw new ArgumentException("Invalid channel.");
            if (!Regex.IsMatch(session, "^[a-f0-9]{8}$")) throw new ArgumentException("Invalid log session.");
            if (maxBytes < 1024) throw new ArgumentOutOfRangeException("maxBytes");
            this.root = CheckedRoot(root); this.service = service; this.channel = channel;
            this.session = session; this.maxBytes = maxBytes;
        }

        public void WriteLine(string line, DateTimeOffset utc)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(line + Environment.NewLine);
            lock (sync)
            {
                AdvanceDate(utc);
                if (file == null || file.Length > 0 && bytes.Length > maxBytes - file.Length) Open();
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int length = Math.Min(bytes.Length - offset, maxBytes - (int)file.Length);
                    // A single unusually large record may span parts, but no UTF-8 character is cut.
                    if (offset + length < bytes.Length)
                        while (length > 0 && (bytes[offset + length] & 0xC0) == 0x80) length--;
                    if (length == 0) { Open(); continue; }
                    file.Write(bytes, offset, length); file.Flush(); offset += length;
                    if (offset < bytes.Length) Open();
                }
            }
        }

        public void AdvanceDate(DateTimeOffset utc)
        {
            lock (sync)
            {
                string nextDay = Moscow(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (day != nextDay) { Close(); day = nextDay; sequence = 0; CurrentPath = null; }
            }
        }

        private void Open()
        {
            Close();
            string directory = Path.Combine(root, day.Substring(0, 7));
            Directory.CreateDirectory(directory);
            RejectLink(directory);
            do
            {
                sequence++;
                CurrentPath = Path.Combine(directory, service + "-" + day + "-" + session + "."
                    + sequence.ToString("D3", CultureInfo.InvariantCulture) + "." + channel + ".log");
            } while (File.Exists(CurrentPath));
            // Readers can search an active file; exclusive cleanup probes and deletion cannot open it.
            file = new FileStream(CurrentPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
        private void Close() { if (file != null) { file.Dispose(); file = null; } }
        public void Dispose() { lock (sync) Close(); }

        public static DateTimeOffset Moscow(DateTimeOffset utc)
        {
            return TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time"));
        }
        public static string CheckedRoot(string path)
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // Check every existing ancestor: neither a log root nor a month may escape through a junction.
            for (DirectoryInfo dir = new DirectoryInfo(full); dir != null; dir = dir.Parent)
                if (dir.Exists) RejectLink(dir.FullName);
            Directory.CreateDirectory(full);
            RejectLink(full);
            return full;
        }
        private static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Log paths must not contain reparse points.");
        }

        public static int Cleanup(string root, DateTimeOffset utc)
        {
            root = CheckedRoot(root);
            DateTime cutoff = utc.UtcDateTime.AddDays(-7);
            int removed = CleanupDirectory(root, cutoff, true);
            foreach (string directory in Directory.GetDirectories(root))
            {
                if (!Regex.IsMatch(Path.GetFileName(directory), "^[0-9]{4}-[0-9]{2}$")
                    || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                removed += CleanupDirectory(directory, cutoff, false);
            }
            return removed;
        }

        private static int CleanupDirectory(string directory, DateTime cutoff, bool legacy)
        {
            string pattern = legacy
                ? "^(server|worker)-[0-9]{8}-[0-9]{6}-[a-f0-9]{8}\\.(out|err)\\.log$"
                : "^(server|worker)-[0-9]{4}-[0-9]{2}-[0-9]{2}-[a-f0-9]{8}\\.[0-9]{3,}\\.(out|err)\\.log$";
            int removed = 0;
            foreach (string path in Directory.GetFiles(directory, "*.log"))
            {
                if (!Regex.IsMatch(Path.GetFileName(path), pattern)
                    || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                try
                {
                    // Last WRITE protects recent incidents in old, long-running legacy files.
                    if (File.GetLastWriteTimeUtc(path) >= cutoff) continue;
                    using (var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        if (File.GetLastWriteTimeUtc(path) >= cutoff) continue;
                    File.Delete(path); removed++;
                }
                catch (IOException) { } // Active file, concurrent cleaner, or transient filesystem failure.
                catch (UnauthorizedAccessException) { }
            }
            return removed;
        }
    }

    public static class LogProcess
    {
        public static int Run(string executable, string workingDirectory, string root, string service)
        {
            root = DailyLog.CheckedRoot(root);
            string session = Guid.NewGuid().ToString("N").Substring(0, 8);
            using (var stdout = new DailyLog(root, service, "out", session, 10 * 1024 * 1024))
            using (var stderr = new DailyLog(root, service, "err", session, 10 * 1024 * 1024))
            using (var process = new Process())
            {
                var sync = new object();
                Exception failure = null;
                Action<DailyLog, string> write = (log, line) =>
                {
                    if (line == null) return;
                    try { log.WriteLine(line, DateTimeOffset.UtcNow); }
                    catch (Exception error) { lock (sync) { if (failure == null) failure = error; } }
                };
                process.StartInfo = new ProcessStartInfo(executable) {
                    WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
                };
                process.OutputDataReceived += (sender, args) => write(stdout, args.Data);
                process.ErrorDataReceived += (sender, args) => write(stderr, args.Data);
                DateTimeOffset nextCleanup = DateTimeOffset.MinValue;
                if (!process.Start()) throw new InvalidOperationException("Production child did not start.");
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                try
                {
                    while (true)
                    {
                        bool exited = process.WaitForExit(1000);
                        stdout.AdvanceDate(DateTimeOffset.UtcNow); stderr.AdvanceDate(DateTimeOffset.UtcNow);
                        lock (sync)
                            if (failure != null) throw new IOException("Production log write failed.");
                        if (DateTimeOffset.UtcNow >= nextCleanup)
                        {
                            try { DailyLog.Cleanup(root, DateTimeOffset.UtcNow); }
                            catch (IOException) { write(stderr, "{\"Category\":\"ProductionLogging\",\"Code\":\"LOG_CLEANUP_IO\"}"); }
                            catch (UnauthorizedAccessException) { write(stderr, "{\"Category\":\"ProductionLogging\",\"Code\":\"LOG_CLEANUP_ACCESS\"}"); }
                            nextCleanup = DateTimeOffset.UtcNow.AddHours(1);
                        }
                        if (exited) break;
                    }
                    process.WaitForExit(); // Also drain asynchronous stdout/stderr before returning the exit code.
                    lock (sync)
                        if (failure != null) throw new IOException("Production log write failed.");
                    return process.ExitCode;
                }
                finally
                {
                    // A managed runner failure must stop its own child; deployment also explicitly stops installed children.
                    if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
                }
            }
        }
    }
}
