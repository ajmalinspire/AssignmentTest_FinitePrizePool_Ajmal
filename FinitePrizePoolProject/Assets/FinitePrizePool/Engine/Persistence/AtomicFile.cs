using System.IO;
using System.Text;

namespace FinitePrizePool.Engine.Persistence
{
    /// <summary>
    /// Crash-safe file writes (§3.3). The new content goes to "name.tmp" and is flushed to disk; only then is
    /// it swapped in with File.Replace, which keeps the previous version as "name.bak". A crash at any moment
    /// leaves either the old file or the new one, never a half-written one.
    /// </summary>
    public static class AtomicFile
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void WriteAllText(string path, string contents)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(path)) File.Replace(temp, path, BackupPath(path), true);
            else File.Move(temp, path);
        }

        /// <summary>The file's text, or null if it has never been written.</summary>
        public static string ReadAllText(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path, Utf8NoBom) : null;
        }

        /// <summary>The previous version kept by the last replace, or null.</summary>
        public static string ReadBackup(string path)
        {
            var backup = BackupPath(path);
            return File.Exists(backup) ? File.ReadAllText(backup, Utf8NoBom) : null;
        }

        public static string BackupPath(string path) => path + ".bak";
    }
}
