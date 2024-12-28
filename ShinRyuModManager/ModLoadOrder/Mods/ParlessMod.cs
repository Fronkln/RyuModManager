using System.IO;
using System.Collections.Generic;

using Utils;

namespace ShinRyuModManager.ModLoadOrder.Mods
{
    public class ParlessMod : Mod
    {
        public List<(string, int)> ParlessFolders { get; }

        public ParlessMod()
            : base(Constants.PARLESS_NAME, 0)
        {
            this.ParlessFolders = new List<(string, int)>();
        }

        public new void PrintInfo()
        {
            this.console.WriteLineIfVerbose();

            if (this.ParlessFolders.Count > 0)
            {
                this.console.WriteLine($"Added {this.ParlessFolders.Count} .parless path(s)");
            }

            base.PrintInfo();
        }

        /// <summary>
        /// Adds all files inside .parless folders in this path into the Files list.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="check"></param>
        public new void AddFiles(Game game, string path, string check)
        {
            if (string.IsNullOrEmpty(check))
            {
                check = this.CheckFolder(path);
            }

            int index = path.IndexOf(".parless");

            if (index != -1)
            {
                // Call the base class AddFiles method
                base.AddFiles(game, path, check);

                // Remove ".parless" from the path
                path = path.Remove(index, 8);

                // Add .parless folders to the list to make it easier to check for them in the ASI
                string loosePath = GamePath.RemoveParlessPath(game, path);
                this.ParlessFolders.Add((loosePath, index - GamePath.GetDataPath(game).Length));

                this.console.WriteLineIfVerbose($"Adding .parless path: {loosePath}");
            }
            else
            {
                // Continue recursing until we find the next ".parless"
                foreach (string folder in Directory.GetDirectories(path))
                {
                    this.AddFiles(game, folder, check);
                }
            }
        }
    }
}
