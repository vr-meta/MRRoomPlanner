using System.IO;

namespace RoomPlanner.Core.Project
{
    /// <summary>
    /// Crash-safe project file IO (audit 2026-08-10, 12 §Б1). The autosave used to
    /// WriteAllText straight over the only copy: a kill mid-write left a truncated
    /// JSON, the next launch caught the parse error, started empty — and the first
    /// pause overwrote the evidence. Now every save goes through a temp file with an
    /// atomic replace, the previous version survives as .bak, and a corrupt main is
    /// quarantined instead of silently recycled.
    /// </summary>
    public static class ProjectFileIO
    {
        public const string BackupSuffix = ".bak";
        public const string CorruptSuffix = ".corrupt";

        public static string BackupPath(string path) => path + BackupSuffix;

        /// <summary>Write via temp + atomic replace; the previous version becomes .bak.</summary>
        public static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Replace(tmp, path, BackupPath(path));
            else File.Move(tmp, path);
        }

        /// <summary>Set a corrupt main file aside so the next save cannot destroy the evidence.</summary>
        public static void QuarantineCorrupt(string path)
        {
            if (!File.Exists(path)) return;
            string dst = path + CorruptSuffix;
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(path, dst);
        }
    }
}
