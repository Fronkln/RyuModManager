using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using ParLibrary.Converter;
using Utils;
using Yarhl.FileSystem;

namespace ShinRyuModManager
{
    public static class ParRepacker
    {

        //Necessary to avoid rare UnauthorizedException errors.
        //https://stackoverflow.com/a/8521573
        public static void DeleteDirectory(string targetDir)
        {
            File.SetAttributes(targetDir, FileAttributes.Normal);

            string[] files = Directory.GetFiles(targetDir);
            string[] dirs = Directory.GetDirectories(targetDir);

            foreach (string file in files)
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string dir in dirs)
            {
                DeleteDirectory(dir);
            }

            Directory.Delete(targetDir, false);
        }

        public static void RemoveOldRepackedPars()
        {
            string pathToParlessMods = Path.Combine(GamePath.GetModsPath(), "Parless");

            if (Directory.Exists(pathToParlessMods))
            {
                Console.Write("Removing old pars...");
                try
                {
                    DeleteDirectory(pathToParlessMods);
                }
                catch
                {
                    Console.WriteLine($" FAIL! {pathToParlessMods}\n");
                }
                Console.WriteLine(" DONE!\n");
            }
        }

        public static async Task RepackDictionary(Game game, Dictionary<string, List<string>> parDictionary)
        {
            var parTasks = new List<Task<ConsoleOutput>>();

            // Clean up the /mods/Parless/ directory before processing the dictionary
            RemoveOldRepackedPars();

            if (parDictionary.Count == 0)
            {
                Console.WriteLine("No pars to repack\n");
            }
            else
            {
                Console.WriteLine("Repacking pars...\n");

                foreach (KeyValuePair<string, List<string>> parModPair in parDictionary)
                {
                    ConsoleOutput consoleOutput = new ConsoleOutput(2);
                    parTasks.Add(Task.Run(() => RepackPar(game, parModPair.Key, parModPair.Value, consoleOutput)));
                }

                while (parTasks.Count > 0)
                {
                    var console = await Task.WhenAny(parTasks).ConfigureAwait(false);

                    if (console != null)
                    {
                        console.Result.Flush();
                    }

                    parTasks.Remove(console);
                }

                Console.WriteLine($"Repacked {parDictionary.Count} par(s)!\n");
            }
        }

        private static ConsoleOutput RepackPar(Game game, string parPath, List<string> mods, ConsoleOutput console)
        {
            parPath = parPath.TrimStart(Path.DirectorySeparatorChar);
            string parPathReal = GamePath.GetRootParPath(game, parPath + ".par");

            string pathToPar = Path.Combine(GamePath.GetDataPath(game), parPathReal);
            string pathToModPar = Path.Combine(GamePath.GetModsPath(), "Parless", parPath + ".par");

            // Check if actual repackable par is nested
            if (parPath + ".par" != parPathReal)
            {
                // -4 to skip ".par", +1 to skip the directory separator
                parPathReal = parPath.Substring(parPathReal.Length - 4 + 1) + ".par";
            }
            else
            {
                // Add the directory separator to skip searching for the node
                parPathReal = "/" + parPathReal;
            }

            // Replace directory separators to properly search for the node
            parPathReal = parPathReal.Replace('\\', '/');

            // Dictionary of fileInPar, ModName
            Dictionary<string, string> fileDict = new Dictionary<string, string>();

            console.WriteLine($"Repacking {parPath + ".par"} ...");

            // Populate fileDict with the files inside each mod
            foreach (string mod in mods)
            {
                foreach (string modFile in GetModFiles(game, parPath, mod, console))
                {
                    if (!fileDict.ContainsKey(modFile))
                    {
                        fileDict.Add(modFile, mod);
                    }
                }
            }

            string pathToTempPar = pathToModPar + "temp";

            if (File.Exists(pathToModPar))
            {
                // Make sure that the .partemp directory is empty
                File.Delete(pathToModPar);
            }

            if (Directory.Exists(pathToTempPar))
            {
                // Make sure that the .partemp directory is empty
                DeleteDirectory(pathToTempPar);
                Directory.CreateDirectory(pathToTempPar);
            }
            else
            {
                Directory.CreateDirectory(pathToTempPar);
            }

            string fileInModFolder;
            string fileInTempFolder;

            // Copy each file in the mods to the .partemp directory
            foreach (KeyValuePair<string, string> fileModPair in fileDict)
            {
                if (fileModPair.Value.StartsWith(Constants.PARLESS_NAME))
                {
                    // 15 = ParlessMod.NAME.Length + 1
                    fileInModFolder = Path.Combine(GamePath.GetDataPath(game), parPath.Insert(int.Parse(fileModPair.Value.Substring(15)) - 1, ".parless"), fileModPair.Key);
                }
                else
                {
                    fileInModFolder = GamePath.GetModPathFromDataPath(fileModPair.Value, Path.Combine(parPath, fileModPair.Key));
                }

                fileInTempFolder = Path.Combine(pathToTempPar, fileModPair.Key);

                Directory.GetParent(fileInTempFolder).Create();
                File.Copy(fileInModFolder, fileInTempFolder, true);
            }

            ParArchiveReaderParameters readerParameters = new ParArchiveReaderParameters
            {
                Recursive = true,
            };

            ParArchiveWriterParameters writerParameters = new ParArchiveWriterParameters
            {
                CompressorVersion = 0,
                OutputPath = pathToModPar,
                IncludeDots = true,
            };

            // Create a node from the .partemp directory and write the par to pathToModPar
            string nodeName = new DirectoryInfo(pathToTempPar).Name;
            Node node = ReadDirectory(pathToTempPar, nodeName);

            // Store a reference to the nodes in the container to dispose of them later, as they are not disposed properly
            NodeContainerFormat containerNode = node.GetFormatAs<NodeContainerFormat>();

            Node par = NodeFactory.FromFile(pathToPar, Yarhl.IO.FileOpenMode.Read);
            par.TransformWith<ParArchiveReader, ParArchiveReaderParameters>(readerParameters);

            Node searchResult = null;

            // Search for the par if it's not the actual loaded par
            if (par.Name != new FileInfo(parPathReal).Name)
            {
                searchResult = SearchParNode(par, parPathReal.ToLowerInvariant());

                if (searchResult == null)
                {
                    // Create an empty node to transfer the children to
                    searchResult = NodeFactory.CreateContainer("empty");
                    searchResult.Add(NodeFactory.CreateContainer("."));
                }

                // Swap the search result and its parent
                Node temp = par;
                par = searchResult;
                searchResult = temp;
            }

            writerParameters.IncludeDots = par.Children[0].Name == ".";

            containerNode.MoveChildrenTo(writerParameters.IncludeDots ? par.Children[0] : par, true);
            par.SortChildren((x, y) => string.CompareOrdinal(x.Name.ToLowerInvariant(), y.Name.ToLowerInvariant()));

            writerParameters.IncludeDots = false;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pathToModPar)));
            par.TransformWith<ParArchiveWriter, ParArchiveWriterParameters>(writerParameters);
            par.Dispose();

            // Dispose of the parent nodes if they exist
            searchResult?.Dispose();

            node.Dispose();
            containerNode.Root.Dispose();

            // Remove the .partemp directory
            DeleteDirectory(pathToTempPar);

            console.WriteLineIfVerbose();
            console.WriteLine($"Repacked {fileDict.Count} file(s) in {parPath + ".par"}!");

            return console;
        }

        public static List<string> GetModFiles(Game game, string par, string mod, ConsoleOutput console)
        {
            List<string> result;
            if (mod.StartsWith(Constants.PARLESS_NAME))
            {
                // Get index of ".parless" in par path
                // 15 = ParlessMod.NAME.Length + 1
                result = GetModFiles(game, Path.Combine(GamePath.GetDataPath(game), par.Insert(int.Parse(mod.Substring(15)) - 1, ".parless")), console);
            }
            else
            {
                result = GetModFiles(game, GamePath.GetModPathFromDataPath(mod, par), console);
            }

            // Get file path relative to par
            return result.Select(f => f.Replace(".parless", "").Substring(f.Replace(".parless", "").IndexOf(par) + par.Length + 1)).ToList();
        }

        private static List<string> GetModFiles(Game game, string path, ConsoleOutput console)
        {
            List<string> files = new List<string>();

            // Add files in current directory
            foreach (string p in Directory.GetFiles(path).Where(f => !f.EndsWith(Constants.VORTEX_MANAGED_FILE)).Select(f => GamePath.GetDataPathFrom(game, f)))
            {
                files.Add(p);
                console.WriteLineIfVerbose($"Adding file: {p}");
            }

            // Get files for all subdirectories
            foreach (string folder in Directory.GetDirectories(path))
            {
                files.AddRange(GetModFiles(game, folder, console));
            }

            return files;
        }

        private static Node ReadDirectory(string dirPath, string nodeName = "")
        {
            dirPath = Path.GetFullPath(dirPath);

            if (string.IsNullOrEmpty(nodeName))
            {
                nodeName = Path.GetFileName(dirPath);
            }

            Node container = NodeFactory.CreateContainer(nodeName);
            var directoryInfo = new DirectoryInfo(dirPath);
            container.Tags["DirectoryInfo"] = directoryInfo;

            var files = directoryInfo.GetFiles();
            foreach (FileInfo file in files)
            {
                Node fileNode = NodeFactory.FromFile(file.FullName, Yarhl.IO.FileOpenMode.Read);
                container.Add(fileNode);
            }

            var directories = directoryInfo.GetDirectories();
            foreach (DirectoryInfo directory in directories)
            {
                Node directoryNode = ReadDirectory(directory.FullName);
                container.Add(directoryNode);
            }

            return container;
        }

        private static Node SearchParNode(Node node, string path)
        {
            string[] paths = path.Split(
                new[] { NodeSystem.PathSeparator },
                StringSplitOptions.RemoveEmptyEntries);

            if (paths.Length == 0)
            {
                return null;
            }

            return SearchParNode(node, paths, 0);
        }

        private static Node SearchParNode(Node node, string[] paths, int pathIndex)
        {
            if (pathIndex < paths.Length)
            {
                string path = paths[pathIndex];

                foreach (Node child in node.Children)
                {
                    if (child.Name == ".")
                    {
                        return SearchParNode(child, paths, pathIndex);
                    }

                    if (child.Name == path || child.Name == (path + ".par"))
                    {
                        return SearchParNode(child, paths, pathIndex + 1);
                    }
                }

                return null;
            }

            return node;
        }
    }
}
