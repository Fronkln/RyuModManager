using ShinRyuModManager.ModLoadOrder;
using ShinRyuModManager.ModLoadOrder.Mods;
using ShinRyuModManager.Templates;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IniParser.Model;
using IniParser;
using Utils;
using YamlDotNet.Serialization;
using static Utils.Constants;
using static Utils.GamePath;
using ParLibrary.Converter;
using YamlDotNet.Core;
using Yarhl.FileSystem;

namespace ShinRyuModManager
{
    public static class Program
    {
        private const string Kernel32Dll = "kernel32.dll";

        [DllImport(Kernel32Dll)]
        private static extern bool AllocConsole();

        [DllImport(Kernel32Dll)]
        private static extern bool FreeConsole();

        [DllImport(Kernel32Dll, SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(int directoryFlags);


        private static bool externalModsOnly = true;
        private static bool looseFilesEnabled = false;
        private static bool cpkRepackingEnabled = false;
        private static bool checkForUpdates = true;
        private static bool isSilent = false;
        private static bool migrated = false;

        public static bool RebuildMLO = true;
        public static bool IsRebuildMLOSupported = true;


        [STAThread]
        public static void Main(string[] args)
        {
            // Try to prevent DLL hijacking by limiting the DLL search path to System32. This should avoid the GUI from crashing due to mod injections.
            // https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-setdefaultdlldirectories
            if (!SetDefaultDllDirectories(0x00000800)) // 0x00000800 corresponds to %windows%\system32
            {
                Console.WriteLine($"Failed to set DLL search path.\nError: {Marshal.GetLastWin32Error()}");
            }

            // Check if left ctrl is pressed to open in CLI (legacy) mode
            if (args.Length == 0 && !Keyboard.IsKeyDown(Key.LeftCtrl))
            {
                // Read the mod list (and execute other RMM stuff)
                List<ModInfo> mods = PreRun();

                // This should be called only after PreRun() to make sure the ini value was loaded
                if (Program.ShouldBeExternalOnly())
                {
                    MessageBox.Show(
                        "External mods folder detected. Please run Shin Ryu Mod Manager in CLI mode " +
                        "(use --cli parameter) and use the external mod manager instead.",
                        "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (Program.ShouldCheckForUpdates())
                {
                    new Thread(delegate () {
                        CheckForUpdatesGUI();
                    }).Start();
                }

                if (Program.ShowWarnings())
                {
                    // Check if the ASI loader is not in the directory (possibly due to incorrect zip extraction)
                    if (Program.MissingDLL())
                    {
                        MessageBox.Show(
                            DINPUT8DLL + " is missing from this directory. Mods will NOT be applied without this file.",
                            "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    // Check if the ASI is not in the directory
                    if (Program.MissingASI())
                    {
                        MessageBox.Show(
                            ASI + " is missing from this directory. Mods will NOT be applied without this file.",
                            "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    // Calculate the checksum for the game's exe to inform the user if their version might be unsupported
                    if (Program.InvalidGameExe())
                    {
                        MessageBox.Show(
                            "Game version is unrecognized. Please use the latest Steam version of the game. " +
                            "The mod list will still be saved.\nMods may still work depending on the version.",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                // Seasonal Event
                if (DateTime.Now.ToString("dd/MM/yy", CultureInfo.InvariantCulture) == "01/04/24")
                {
                    if (!Util.CheckFlag(Settings.EVENT_FOOLS24_FLAG_FILE_NAME))
                    {
                        Miscellaneous.Fools24.Fools24Window fools24Window = new Miscellaneous.Fools24.Fools24Window();
                        fools24Window.ShowDialog();
                        Util.CreateFlag(Settings.EVENT_FOOLS24_FLAG_FILE_NAME);
                    }
                }
                else
                {
                    Util.DeleteFlag(Settings.EVENT_FOOLS24_FLAG_FILE_NAME);
                }


                MainWindow window = new MainWindow();
                App app = new App();
                app.Run(window);
            }
            else
            {
                bool consoleEnabled = true;

                foreach (string a in args)
                {
                    if (a == "-s" || a == "--silent")
                    {
                        consoleEnabled = false;
                        break;
                    }
                }

                if (consoleEnabled)
                    AllocConsole();

                MainCLI(args).Wait();

                if (consoleEnabled)
                    FreeConsole();
            }
        }


        internal static void CheckForUpdatesGUI(bool notifyResult = false)
        {
            string currentPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            string updaterPath = Path.Combine(currentPath, Settings.UPDATER_EXECUTABLE_NAME);
            string updateFlagPath = Path.Combine(currentPath, Settings.UPDATE_FLAG_FILE_NAME);
            bool updaterResult = false;

            if (File.Exists(updaterPath))
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(updaterPath);
                string version = versionInfo.FileVersion;
                updaterResult = UpdateUpdater(updaterPath, version);
            }
            else //Updater not present. Download latest
            {
                updaterResult = UpdateUpdater(updaterPath);
            }

            if (updaterResult)
            {
                Process proc = new Process();
                proc.StartInfo.FileName = updaterPath;
                proc.StartInfo.Arguments = $"-v {Util.GetAppVersion()} -c";
                proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                proc.Start();
                proc.WaitForExit();
            }

            if (File.Exists(updateFlagPath))
            {
                string updateVersion = File.ReadAllText(updateFlagPath);
                File.Delete(updateFlagPath);
                MessageBoxResult result = MessageBox.Show($"Shin Ryu Mod Manager version {updateVersion} is available for download.\nWould you like to update now?", "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                {
                    Process proc = new Process();
                    proc.StartInfo.FileName = updaterPath;
                    proc.StartInfo.Arguments = $"-v {Util.GetAppVersion()}";
                    proc.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
                    proc.Start();
                    Environment.Exit(0x55504454); //UPDT
                }
                else if (result == MessageBoxResult.No)
                {
                    return;
                }
            }
            else if (notifyResult)
            {
                MessageBox.Show("No SRMM updates available.", "No updates", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }


        private static bool UpdateUpdater(string updaterPath, string currentVersion = "0.0.0")
        {
            try
            {
                WebClient client = new WebClient();
                string yamlString = client.DownloadString($"https://raw.githubusercontent.com/{Settings.UPDATE_INFO_REPO_OWNER}/{Settings.UPDATE_INFO_REPO}/main/{Settings.UPDATE_INFO_FILE_PATH}");

                var deserializer = new DeserializerBuilder().Build();
                var yamlObject = deserializer.Deserialize<Updater>(yamlString);

                bool isHigher = Util.CompareVersionIsHigher(yamlObject.Version, currentVersion);
                if (isHigher)
                {
                    client.DownloadFile(yamlObject.Download, updaterPath);
                    MessageBox.Show($"RyuUpdater has been updated to version {yamlObject.Version}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                client.Dispose();
                return true;
            }
            catch (WebException)
            {
                MessageBox.Show("Could not fetch update data.\nThis could be a problem with your internet connection or GitHub.\nPlease try again later.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }


        public static async Task MainCLI(string[] args)
        {
            Console.WriteLine($"Shin Ryu Mod Manager v{AssemblyVersion.GetVersion()}");
            Console.WriteLine($"By SRMM Studio (a continuation of SutandoTsukai181's work)\n");

            // Parse arguments
            List<string> list = new List<string>(args);

            if (list.Contains("-h") || list.Contains("--help"))
            {
                Console.WriteLine("Usage: run without arguments to generate mod load order.");
                Console.WriteLine("       run with \"-s\" or \"--silent\" flag to prevent checking for updates and remove prompts.");
                Console.WriteLine("       run with \"-r\" or \"--run\" flag to run the game after the program finishes.");
                Console.WriteLine("       run with \"-h\" or \"--help\" flag to show this message and exit.");

                return;
            }

            if (list.Contains("-s") || list.Contains("--silent"))
            {
                isSilent = true;
            }

            await RunGeneration(ConvertNewToOldModList(PreRun())).ConfigureAwait(true);
            await PostRun().ConfigureAwait(true);

            if (list.Contains("-r") || list.Contains("--run"))
            {
                // Run game
                if (File.Exists(GetGameExe()))
                {
                    Console.WriteLine($"Launching \"{GetGameExe()}\"...");
                    Process.Start(GetGameExe());
                }
                else
                {
                    Console.WriteLine($"Warning: Could not run game because \"{GetGameExe()}\" does not exist.");
                }
            }
        }


        public static List<ModInfo> PreRun()
        {
            var iniParser = new FileIniDataParser();
            iniParser.Parser.Configuration.AssigmentSpacer = string.Empty;

            Game game = GamePath.GetGame();

            IniData ini;
            if (File.Exists(INI))
            {
                ini = iniParser.ReadFile(INI);

                if (ini.TryGetKey("Debug.CPKRepackingTest", out string cpkRepatch))
                {
                    cpkRepackingEnabled = int.Parse(cpkRepatch) == 1;
                }

                if (ini.TryGetKey("Overrides.LooseFilesEnabled", out string looseFiles))
                {
                    looseFilesEnabled = int.Parse(looseFiles) == 1;
                }

                if (ini.TryGetKey("RyuModManager.Verbose", out string verbose))
                {
                    ConsoleOutput.Verbose = int.Parse(verbose) == 1;
                }

                if (ini.TryGetKey("RyuModManager.CheckForUpdates", out string check))
                {
                    checkForUpdates = int.Parse(check) == 1;
                }

                if (ini.TryGetKey("RyuModManager.ShowWarnings", out string showWarnings))
                {
                    ConsoleOutput.ShowWarnings = int.Parse(showWarnings) == 1;
                }

                if (ini.TryGetKey("RyuModManager.LoadExternalModsOnly", out string extMods))
                {
                    externalModsOnly = int.Parse(extMods) == 1;
                }

                if (ini.TryGetKey("Overrides.RebuildMLO", out string rebuildMLO))
                {
                    RebuildMLO = int.Parse(rebuildMLO) == 1;
                }

                if (!ini.TryGetKey("Parless.IniVersion", out string iniVersion) || int.Parse(iniVersion) < ParlessIni.CurrentVersion)
                {
                    // Update if ini version is old (or does not exist)
                    Console.Write(INI + " is outdated. Updating ini to the latest version... ");

                    if (int.Parse(iniVersion) <= 3)
                    {
                        // Force enable RebuildMLO option
                        ini.Sections["Overrides"]["RebuildMLO"] = "1";
                        RebuildMLO = true;
                    }

                    iniParser.WriteFile(INI, IniTemplate.UpdateIni(ini));
                    Console.WriteLine("DONE!\n");
                }
            }
            else
            {
                // Create ini if it does not exist
                Console.Write(INI + " was not found. Creating default ini... ");
                iniParser.WriteFile(INI, IniTemplate.NewIni());
                Console.WriteLine("DONE!\n");
            }

            if (game != Game.Unsupported && !Directory.Exists(MODS))
            {
                if (!Directory.Exists(MODS))
                {
                    // Create mods folder if it does not exist
                    Console.Write($"\"{MODS}\" folder was not found. Creating empty folder... ");
                    Directory.CreateDirectory(MODS);
                    Console.WriteLine("DONE!\n");
                }

                if (!Directory.Exists(LIBRARIES))
                {
                    // Create libraries folder if it does not exist
                    Console.Write($"\"{LIBRARIES}\" folder was not found. Creating empty folder... ");
                    Directory.CreateDirectory(LIBRARIES);
                    Console.WriteLine("DONE!\n");
                }
            }

            // TODO: Maybe move this to a separate "Game patches" file
            // Virtua Fighter eSports crashes when used with dinput8.dll as the ASI loader
            if (game == Game.eve && File.Exists(DINPUT8DLL))
            {
                if (File.Exists(VERSIONDLL))
                {
                    Console.Write($"Game specific patch: Deleting {DINPUT8DLL} because {VERSIONDLL} exists...");

                    // Remove dinput8.dll
                    File.Delete(DINPUT8DLL);
                }
                else
                {
                    Console.Write($"Game specific patch: Renaming {DINPUT8DLL} to {VERSIONDLL}...");

                    // Rename dinput8.dll to version.dll to prevent the game from crashing
                    File.Move(DINPUT8DLL, VERSIONDLL);
                }

                Console.WriteLine(" DONE!\n");
            }
            else if (game >= Game.Judgment && game != Game.likeadragongaiden)
            {
                // Lost Judgment (and Judgment post update 1) does not like Ultimate ASI Loader, so instead we use a custom build of DllSpoofer (https://github.com/Kazurin-775/DllSpoofer)
                if (File.Exists(DINPUT8DLL))
                {
                    Console.Write($"Game specific patch: Deleting {DINPUT8DLL} because it causes crashes with Judgment games...");

                    // Remove dinput8.dll
                    File.Delete(DINPUT8DLL);

                    Console.WriteLine(" DONE!\n");
                }

                if (!File.Exists(WINMMDLL))
                {
                    if (File.Exists(WINMMLJ))
                    {
                        Console.Write($"Game specific patch: Enabling {WINMMDLL} by renaming {WINMMLJ} to fix Judgment games crashes...");

                        // Rename dinput8.dll to version.dll to prevent the game from crashing
                        File.Move(WINMMLJ, WINMMDLL);

                        Console.WriteLine(" DONE!\n");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"WARNING: {WINMMLJ} was not found. Judgment games will NOT load mods without this file. Please redownload Shin Ryu Mod Manager.\n");
                        Console.ResetColor();
                    }
                }
            }

            // Read ini (again) to check if we should try importing the old load order file
            ini = iniParser.ReadFile(INI);

            List<ModInfo> mods = new List<ModInfo>();

            if (ShouldBeExternalOnly())
            {
                // Only load the files inside the external mods path, and ignore the load order in the txt
                mods.Add(new ModInfo(EXTERNAL_MODS));

                if (GamePath.GetGame() == Game.Judgment || GamePath.GetGame() == Game.LostJudgment)
                {
                    // Disable RebuildMLO when using an external mod manager
                    if (ini.TryGetKey("Overrides.RebuildMLO", out string _))
                    {
                        Console.Write($"Game specific patch: Disabling RebuildMLO for Judgment and Lost Judgment when using an external mod manager...");

                        ini.Sections["Overrides"]["RebuildMLO"] = "0";
                        iniParser.WriteFile(INI, ini);

                        Console.WriteLine(" DONE!\n");
                    }
                }
            }
            else
            {
                bool defaultEnabled = true;

                if (File.Exists(TXT_OLD) && ini.GetKey("SavedSettings.ModListImported") == null)
                {
                    // Scanned mods should be disabled, because that's how they were with the old txt format
                    defaultEnabled = false;

                    // Set a flag so we can delete the old file after we actually save the mod list
                    migrated = true;

                    // Migrate old format to new
                    Console.Write("Old format load order file (" + TXT_OLD + ") was found. Importing to the new format...");
                    mods.AddRange(ConvertOldToNewModList(ReadModLoadOrderTxt(TXT_OLD)).Where(n => !mods.Any(m => EqualModNames(m.Name, n.Name))));
                    Console.WriteLine(" DONE!\n");
                }
                else if (File.Exists(TXT))
                {
                    mods.AddRange(ReadModListTxt(TXT).Where(n => !mods.Any(m => EqualModNames(m.Name, n.Name))));
                }
                else
                {
                    Console.WriteLine(TXT + " was not found. Will load all existing mods.\n");
                }

                if (Directory.Exists(MODS))
                {
                    // Add all scanned mods that have not been added to the load order yet
                    Console.Write("Scanning for mods...");
                    mods.AddRange(ScanMods().Where(n => !mods.Any(m => EqualModNames(m.Name, n))).Select(m => new ModInfo(m, defaultEnabled)));
                    Console.WriteLine(" DONE!\n");
                }
            }

            if (GamePath.IsXbox(Path.Combine(GetGamePath())))
            {
                if (ini.TryGetKey("Overrides.RebuildMLO", out string _))
                {
                    Console.Write($"Game specific patch: Disabling RebuildMLO for Xbox games...");

                    ini.Sections["Overrides"]["RebuildMLO"] = "0";
                    iniParser.WriteFile(INI, ini);

                    Console.WriteLine(" DONE!\n");
                }
            }

            return mods;
        }


        public static async Task<bool> RunGeneration(List<string> mods)
        {
            if (File.Exists(Utils.Constants.MLO))
            {
                Console.Write("Removing old MLO...");

                // Remove existing MLO file to avoid it being used if a new MLO won't be generated
                File.Delete(Utils.Constants.MLO);

                Console.WriteLine(" DONE!\n");
            }

            // Remove previously repacked pars, to avoid unwanted side effects
            ParRepacker.RemoveOldRepackedPars();

            if (GamePath.GetGame() != Game.Unsupported)
            {
                if (mods?.Count > 0 || looseFilesEnabled)
                {
                    MLO result =  await Generator.GenerateModLoadOrder(mods, looseFilesEnabled, cpkRepackingEnabled).ConfigureAwait(false);

                    if (GameModel.SupportsUBIK(GamePath.GetGame()))
                    {
                        GameModel.DoUBIKProcedure(result);
                    }



                    return true;
                }

                Console.WriteLine("Aborting: No mods were found, and .parless paths are disabled\n");
            }
            else
            {
                Console.WriteLine("Aborting: No supported game was found in this directory\n");
            }

            return false;
        }


        public static async Task PostRun()
        {
            // Check if the ASI loader is not in the directory (possibly due to incorrect zip extraction)
            if (MissingDLL())
            {
                Console.WriteLine($"Warning: \"{DINPUT8DLL}\" is missing from this directory. Shin Ryu Mod Manager will NOT function properly without this file\n");
            }

            // Check if the ASI is not in the directory
            if (MissingASI())
            {
                Console.WriteLine($"Warning: \"{ASI}\" is missing from this directory. Shin Ryu Mod Manager will NOT function properly without this file\n");
            }

            // Calculate the checksum for the game's exe to inform the user if their version might be unsupported
            if (ConsoleOutput.ShowWarnings && InvalidGameExe())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Warning: Game version is unrecognized. Please use the latest Steam version of the game.");
                Console.WriteLine($"Shin Ryu Mod Manager will still generate the load order, but the game might CRASH or not function properly\n");
                Console.ResetColor();
            }

            if (!isSilent)
            {
                Console.WriteLine("Program finished. Press any key to exit...");
                Console.ReadKey();
            }
        }


        public static bool ShowWarnings()
        {
            return ConsoleOutput.ShowWarnings;
        }


        public static bool MissingDLL()
        {
            return !(File.Exists(DINPUT8DLL) || File.Exists(VERSIONDLL) || File.Exists(D3D9DLL) || File.Exists(WINMMDLL));
        }


        public static bool MissingASI()
        {
            return !File.Exists(ASI);
        }


        public static bool InvalidGameExe()
        {
            return false;

            /*
            string path = Path.Combine(GetGamePath(), GetGameExe());
            return GetGame() == Game.Unsupported || !GameHash.ValidateFile(path, GetGame());
            */
        }


        /// <summary>
        /// Read the load order from ModLoadOrder.txt (old format).
        /// </summary>
        /// <param name="txt">expected to be "ModLoadOrder.txt".</param>
        /// <returns>list of strings containing mod names according to the load order in the file.</returns>
        public static List<string> ReadModLoadOrderTxt(string txt)
        {
            List<string> mods = new List<string>();

            if (!File.Exists(txt))
            {
                return mods;
            }

            StreamReader file = new StreamReader(new FileInfo(txt).FullName);

            string line;
            while ((line = file.ReadLine()) != null)
            {
                if (!line.StartsWith(";"))
                {
                    line = line.Split(new char[] { ';' }, 1)[0];

                    // Only add existing mods that are not duplicates
                    if (line.Length > 0 && Directory.Exists(Path.Combine(MODS, line)) && !mods.Contains(line))
                    {
                        mods.Add(line);
                    }
                }
            }

            file.Close();

            return mods;
        }


        /// <summary>
        /// Read the mod list from ModList.txt (current format).
        /// </summary>
        /// <param name="txt">expected to be "ModList.txt".</param>
        /// <returns>list of ModInfo for each mod in the file.</returns>
        public static List<ModInfo> ReadModListTxt(string txt)
        {
            List<ModInfo> mods = new List<ModInfo>();

            if (!File.Exists(txt))
            {
                return mods;
            }

            StreamReader file = new StreamReader(new FileInfo(txt).FullName);

            string line = file.ReadLine();

            if (line != null)
            {
                foreach (string mod in line.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (mod.StartsWith("<") || mod.StartsWith(">"))
                    {
                        ModInfo info = new ModInfo(mod.Substring(1), mod[0] == '<');

                        if (ModInfo.IsValid(info) && !mods.Contains(info))
                        {
                            mods.Add(info);
                        }
                    }
                }
            }

            file.Close();

            return mods;
        }


        public static bool SaveModList(List<ModInfo> mods)
        {
            bool result = WriteModListTxt(mods);

            if (migrated)
            {
                try
                {
                    File.Delete(TXT_OLD);

                    var iniParser = new FileIniDataParser();
                    iniParser.Parser.Configuration.AssigmentSpacer = string.Empty;

                    IniData ini = iniParser.ReadFile(INI);
                    ini.Sections.AddSection("SavedSettings");
                    ini["SavedSettings"].AddKey("ModListImported", "true");
                    iniParser.WriteFile(INI, ini);
                }
                catch
                {
                    Console.WriteLine("Could not delete " + TXT_OLD + ". This file should be deleted manually.");
                }
            }

            return result;
        }


        private static bool WriteModListTxt(List<ModInfo> mods)
        {
            // No need to write the file if it's going to be empty
            if (mods?.Count > 0)
            {
                string content = "";

                foreach (ModInfo m in mods)
                {
                    content += "|" + (m.Enabled ? "<" : ">") + m.Name;
                }

                File.WriteAllText(TXT, content.Substring(1));

                return true;
            }

            return false;
        }


        public static List<ModInfo> ConvertOldToNewModList(List<string> mods)
        {
            return mods.Select(m => new ModInfo(m)).ToList();
        }


        public static List<string> ConvertNewToOldModList(List<ModInfo> mods)
        {
            return mods.Where(m => m.Enabled).Select(m => m.Name).ToList();
        }


        public static bool ShouldBeExternalOnly()
        {
            return externalModsOnly && Directory.Exists(GetExternalModsPath());
        }


        public static bool ShouldCheckForUpdates()
        {
            return checkForUpdates;
        }


        private static List<string> ScanMods()
        {
            return Directory.GetDirectories(GetModsPath())
                .Select(d => Path.GetFileName(d.TrimEnd(new char[] { Path.DirectorySeparatorChar })))
                .Where(m => (m != "Parless") && (m != EXTERNAL_MODS))
                .ToList();
        }


        private static bool EqualModNames(string m, string n)
        {
            return string.Compare(m, n, StringComparison.InvariantCultureIgnoreCase) == 0;
        }
    }



    public class Updater
    {
        public string Version { get; set; }
        public string Download {  get; set; }
    }
}
