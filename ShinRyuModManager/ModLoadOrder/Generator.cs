using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ShinRyuModManager.ModLoadOrder.Mods;
using Utils;
using ShinRyuModManager.CPKRepatcher;
using static Utils.ConsoleOutput;

namespace ShinRyuModManager.ModLoadOrder
{
    public static class Generator
    {
        public static async Task<MLO> GenerateModLoadOrder(List<string> mods, bool looseFilesEnabled, bool cpkRepackingEnabled)
        {
            List<int> modIndices = new List<int> { 0 };
            OrderedSet<string> files = new OrderedSet<string>();

            // Dictionary of Mod, ListOfFolders
            Dictionary<string, List<string>> modsWithFoldersNotFound = new Dictionary<string, List<string>>();

            // Dictionary of PathToPar, ListOfMods
            Dictionary<string, List<string>> parDictionary = new Dictionary<string, List<string>>();

            ParlessMod loose = new ParlessMod();

            Game game = GamePath.GetGame();

            if (looseFilesEnabled)
            {
                loose.AddFiles(game, GamePath.GetDataPath(game), string.Empty);

                loose.PrintInfo();

                // Add all pars to the dictionary
                foreach (string par in loose.ParFolders)
                {
                    int index = par.IndexOf(".parless");

                    if (index != -1)
                    {
                        // Remove .parless from the par's path
                        // Since .parless loose files are processed first, we can be sure that the dictionary won't have duplicates
                        parDictionary.Add(par.Remove(index, 8), new List<string> { loose.Name + "_" + index });
                    }
                }

                Console.WriteLine($"Done reading {Constants.PARLESS_NAME}\n");
            }

            Mod[] modsObjects = new Mod[mods.Count];

            Mod mod;
            string modPath;
            string subPathName;
            List<string> foldersNotFound;
            Dictionary<string, List<int>> cpkDictionary = new Dictionary<string, List<int>>();
            Console.WriteLine("Reading mods...\n");

            // TODO: Make mod reading async

            // Use a reverse loop to be able to remove items from the list when necessary
            for (int i = mods.Count - 1; i >= 0; i--)
            {
                mod = new Mod(mods[i]);
                modPath = Path.Combine(GamePath.GetModsPath(), mods[i]);
                mod.AddFiles(game, modPath, string.Empty);

                mod.PrintInfo();

                if (mod.Files.Count > 0 || mod.ParFolders.Count > 0 || mod.CpkFolders.Count > 0)
                {
                    files.UnionWith(mod.Files);
                    modIndices.Add(files.Count);

                    foreach (string folder in mod.CpkFolders)
                    {
                        if (!cpkDictionary.ContainsKey(folder))
                        {
                            cpkDictionary[folder] = new List<int>();
                        }

                        cpkDictionary[folder].Add(mods.Count - 1 - i);
                    }
                }
                else
                {
                    mods.RemoveAt(i);
                }

                // Add all pars to the dictionary
                foreach (string par in mod.ParFolders)
                {
                    List<string> list;
                    if (parDictionary.TryGetValue(par, out list))
                    {
                        // Add the mod's name to the par's list
                        list.Add(mod.Name);
                    }
                    else
                    {
                        // If a par is not in the dictionary, make a new list for it
                        parDictionary.Add(par, new List<string> { mod.Name });
                    }
                }

                // Check for folders which do not exist in the data path in the mod's root
                foldersNotFound = new List<string>();
                foreach (string subPath in Directory.GetDirectories(modPath))
                {
                    subPathName = new DirectoryInfo(subPath).Name;
                    if (!(GamePath.DirectoryExistsInData(game, subPathName) || GamePath.FileExistsInData(game, subPathName + ".par")))
                    {
                        foldersNotFound.Add(subPathName);
                    }
                }

                if (foldersNotFound.Count != 0)
                {
                    modsWithFoldersNotFound.Add(mod.Name, foldersNotFound);
                }

                modsObjects[i] = mod;
            }

            Console.WriteLine($"Added {mods.Count} mod(s) and {files.Count} file(s)!\n");

            // Reverse the list because the last mod in the list should have the highest priority
            mods.Reverse();

            Console.Write($"Generating {Constants.MLO} file...");

            // Generate MLO
            MLO mlo = new MLO(modIndices, mods, files, loose.ParlessFolders, cpkDictionary);
            mlo.WriteMLO(Path.Combine(GamePath.GetGamePath(), Constants.MLO));

            Console.WriteLine(" DONE!\n");

            // Check if a mod has a par that will override the repacked par, and skip repacking it in that case
            int matchIndex;
            foreach (string key in parDictionary.Keys.ToList())
            {
                List<string> value = parDictionary[key];

                // Faster lookup by checking in the OrderedSet
                if (files.Contains(key + ".par"))
                {
                    // Get the mod's index from the ModLoadOrder's Files
                    matchIndex = mlo.Files.Find(f => f.Item1 == key.Replace('\\', '/') + ".par").Item2;

                    // Avoid repacking pars which exist as a file in mods that have a higher priority than the first mod in the par to be repacked
                    if (mods.IndexOf(value[0]) > matchIndex)
                    {
                        parDictionary.Remove(key);
                    }
                }
            }

            Dictionary<string, List<string>> cpkRepackDict = new Dictionary<string, List<string>>();

            foreach(Mod modObj in modsObjects)
            {
                foreach (string str in modObj.RepackCPKs)
                {
                    if (!cpkDictionary.ContainsKey(str))
                        cpkRepackDict.Add(str, new List<string>());

                    cpkRepackDict[str].Add(modObj.Name);
                }
            }

            // Repack pars
            await ParRepacker.RepackDictionary(game, parDictionary).ConfigureAwait(false);

            if(cpkRepackingEnabled)
                await CPKPatcher.RepackDictionary(game, cpkRepackDict).ConfigureAwait(false);

            if (ConsoleOutput.ShowWarnings)
            {
                foreach (string key in modsWithFoldersNotFound.Keys.ToList())
                {
                    Console.WriteLine($"Warning: Some folders in the root of \"{key}\" do not exist in the game's data. Check if the mod was extracted correctly.");

                    if (ConsoleOutput.Verbose)
                    {
                        foreach (string folder in modsWithFoldersNotFound[key])
                        {
                            Console.WriteLine($"Folder not found: {folder}");
                        }
                    }

                    Console.WriteLine();
                }
            }

            return mlo;
        }
    }
}
