using System;
using System.IO;
using Kordon.Core.Data;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// Locates the shipped data files.  The test project links
    /// Assets/StreamingAssets/Data/**/*.json into its output directory, but the tests are
    /// also runnable straight from the repository (and from the Unity Test Framework,
    /// where the working directory is the project root), so both layouts are probed.
    /// </summary>
    public static class TestData
    {
        private static string _dataRoot;
        private static GameDatabase _database;

        public static string DataRoot
        {
            get
            {
                if (_dataRoot != null)
                {
                    return _dataRoot;
                }

                _dataRoot = FindDataRoot();
                return _dataRoot;
            }
        }

        public static GameDatabase Database => _database ?? (_database = GameDatabase.Load(DataRoot));

        public static string DefinitionsDirectory => Path.Combine(DataRoot, "Definitions");

        private static string FindDataRoot()
        {
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory, "Data"),
                Path.Combine(Directory.GetCurrentDirectory(), "Assets", "StreamingAssets", "Data")
            };

            foreach (string candidate in candidates)
            {
                if (Directory.Exists(Path.Combine(candidate, "Definitions")))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            // Walk up from the test binary looking for the repository layout.
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && directory != null; i++)
            {
                string probe = Path.Combine(directory.FullName, "Assets", "StreamingAssets", "Data");
                if (Directory.Exists(Path.Combine(probe, "Definitions")))
                {
                    return Path.GetFullPath(probe);
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate Assets/StreamingAssets/Data/Definitions from " + AppContext.BaseDirectory);
        }
    }
}
