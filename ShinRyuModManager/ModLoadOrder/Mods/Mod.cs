using System.IO;
using System.Collections.Generic;
using System.Linq;

using Utils;

namespace ShinRyuModManager.ModLoadOrder.Mods
{
    public class Mod
    {
        protected readonly ConsoleOutput console;

        public string Name { get; set; }

        /// <summary>
        /// Files that can be directly loaded from the mod path.
        /// </summary>
        public List<string> Files { get; }

        /// <summary>
        /// Folders that have to be repacked into pars before running the game.
        /// </summary>
        public List<string> ParFolders { get; }

        /// <summary>
        /// Folders that need to be bound as a directory to a CPK binder.
        /// </summary>
        public List<string> CpkFolders { get; }

        /// <summary>
        /// Folders that need to be repacked.
        /// </summary>
        public List<string> RepackCPKs { get; }

        public Mod(string name, int indent = 2)
        {
            this.Name = name;
            this.Files = new List<string>();
            this.ParFolders = new List<string>();
            this.CpkFolders = new List<string>();
            this.RepackCPKs = new List<string>();

            this.console = new ConsoleOutput(indent);
            this.console.WriteLine($"Reading directory: {name} ...");
        }

        public void PrintInfo()
        {
            this.console.WriteLineIfVerbose();

            if (this.Files.Count > 0 || this.ParFolders.Count > 0)
            {
                if (this.Files.Count > 0)
                {
                    this.console.WriteLine($"Added {this.Files.Count} file(s)");
                }

                if (this.ParFolders.Count > 0)
                {
                    this.console.WriteLine($"Added {this.ParFolders.Count} folder(s) to be repacked");
                }

                if (this.CpkFolders.Count > 0)
                {
                    this.console.WriteLine($"Added {this.CpkFolders.Count} CPK folder(s) to be bound");
                }
            }
            else
            {
                this.console.WriteLine($"Nothing found for {this.Name}, skipping");
            }

            this.console.Flush();
        }

        public void AddFiles(Game game, string path, string check)
        {
            bool needsRepack = false;
            string basename = GamePath.GetBasename(path);

            // Check if this path does not need repacking
            switch (check)
            {
                case "chara":
                    needsRepack = GamePath.ExistsInDataAsPar(game, path);
                    break;
                case "map_":
                    needsRepack = GamePath.ExistsInDataAsPar(game, path);
                    break;
                case "effect":
                    needsRepack = GamePath.ExistsInDataAsPar(game, path);
                    break;
                case "prep":
                    needsRepack = GamePath.GetGame() < Game.Yakuza0 && GamePath.ExistsInDataAsPar(game, path);
                    break;
                case "light_anim":
                    needsRepack = GamePath.GetGame() < Game.Yakuza0 && GamePath.ExistsInDataAsPar(game, path);
                    break;
                case "2d":
                    needsRepack = (basename.StartsWith("sprite") || basename.StartsWith("pj")) && GamePath.ExistsInDataAsParNested(game, path);
                    break;
                case "cse":
                    needsRepack = (basename.StartsWith("sprite") || basename.StartsWith("pj")) && GamePath.ExistsInDataAsParNested(game, path);
                    break;
                case "pausepar":
                    if (GamePath.GetGame() >= Game.Yakuza0)
                        needsRepack = true;
                    else
                        needsRepack = !basename.StartsWith("pause") && GamePath.ExistsInDataAsPar(game, path);
                    break;
                case "pausepar_e":
                    needsRepack = !basename.StartsWith("pause") && GamePath.ExistsInDataAsPar(game, path);
                    break;
                case "particle":
                    if (GamePath.GetGame() >= Game.Yakuza6 && basename == "arc")
                    {
                        check = "particle/arc";
                    }

                    if (new DirectoryInfo(path).Parent.Name == "arc_list")
                        needsRepack = true;
                    break;
                case "particle/arc":
                    needsRepack = GamePath.ExistsInDataAsParNested(game, path);
                    break;
                case "stage":
                    needsRepack = GamePath.GetGame() == Game.eve && basename == "sct" && GamePath.ExistsInDataAsParNested(game, path);
                    break;
                case "":
                    needsRepack = (basename == "ptc" && GamePath.ExistsInDataAsParNested(game, path))
                        || (basename == "entity_adam" && GamePath.ExistsInDataAsPar(game, path));

                    if (!needsRepack)
                    {
                        check = this.CheckFolder(basename);
                    }
                    break;
                default:
                    break;
            }

            // Check for CPK directories
            string cpkDataPath;
            switch (basename)
            {
                case "bgm":
                    cpkDataPath = GamePath.RemoveModPath(game, path);
                    this.RepackCPKs.Add(cpkDataPath);
                    break;

                case "se":
                case "speech":
                    cpkDataPath = GamePath.RemoveModPath(game, path);
                    if (GamePath.GetGame() == Game.Yakuza5)
                    {
                        this.CpkFolders.Add(cpkDataPath + ".cpk");
                        this.console.WriteLineIfVerbose($"Adding CPK folder: {cpkDataPath}");
                    }
                    else
                    {
                        this.RepackCPKs.Add(cpkDataPath + ".cpk");
                    }

                    break;
                case "stream":
                case "stream_en":
                case "stmdlc":
                case "stmdlc_en":
                case "movie":
                case "moviesd":
                case "moviesd_dlc":
                    cpkDataPath = GamePath.RemoveModPath(game, path);
                    if (GamePath.GetGame() == Game.Judgment || GamePath.GetGame() == Game.LostJudgment)
                    {
                        this.CpkFolders.Add(cpkDataPath + ".par");
                        this.console.WriteLineIfVerbose($"Adding CPK folder: {cpkDataPath}");
                    }

                    break;
                default:
                    break;
            }

            if (needsRepack)
            {
                string dataPath = GamePath.GetDataPathFrom(game, path);

                // Add this folder to the list of folders to be repacked and stop recursing
                this.ParFolders.Add(dataPath);
                this.console.WriteLineIfVerbose($"Adding repackable folder: {dataPath}");
            }
            else
            {
                // Add files in current directory
                foreach (string p in Directory.GetFiles(path).Where(f => !f.EndsWith(Constants.VORTEX_MANAGED_FILE)).Select(f => GamePath.GetDataPathFrom(game, f)))
                {
                    this.Files.Add(p);
                    this.console.WriteLineIfVerbose($"Adding file: {p}");
                }

                // Get files for all subdirectories
                foreach (string folder in Directory.GetDirectories(path))
                {
                    // Break an important rule in the concept of inheritance to make the program function correctly
                    if (this.GetType() == typeof(ParlessMod))
                    {
                        ((ParlessMod)this).AddFiles(game, folder, check);
                    }
                    else
                    {
                        this.AddFiles(game, folder, check);
                    }
                }
            }
        }

        protected string CheckFolder(string name)
        {
            foreach (string folder in Constants.IncompatiblePars)
            {
                if (name.StartsWith(folder))
                {
                    return folder;
                }
            }

            return "";
        }
    }
}
