using System.IO;
using UnityEditor;
using UnityEngine;

namespace FinitePrizePool.EditorTools
{
    /// <summary>
    /// Where the Editor tools write their reports: the repository's Docs folder, one level above the Unity
    /// project (FinitePrizePoolProject). Falls back to a Docs folder inside the project if there is none.
    /// </summary>
    internal static class EditorFiles
    {
        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        public static string DocsFolder
        {
            get
            {
                var repoDocs = Path.Combine(Directory.GetParent(ProjectRoot).FullName, "Docs");
                return Directory.Exists(repoDocs) ? repoDocs : Path.Combine(ProjectRoot, "Docs");
            }
        }

        /// <summary>Asks where to save (defaulting to Docs/fileName) and writes the file. Returns the path, or null if cancelled.</summary>
        public static string SaveWithDialog(string title, string fileName, string contents)
        {
            Directory.CreateDirectory(DocsFolder);
            var extension = Path.GetExtension(fileName).TrimStart('.');
            var path = EditorUtility.SaveFilePanel(title, DocsFolder, Path.GetFileNameWithoutExtension(fileName), extension);
            if (string.IsNullOrEmpty(path)) return null;
            File.WriteAllText(path, contents);
            Debug.Log("[FinitePrizePool] Wrote " + path);
            return path;
        }

        public static string WriteToDocs(string fileName, string contents)
        {
            Directory.CreateDirectory(DocsFolder);
            var path = Path.Combine(DocsFolder, fileName);
            File.WriteAllText(path, contents);
            Debug.Log("[FinitePrizePool] Wrote " + path);
            return path;
        }
    }
}
