using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Utils;

namespace ShinRyuModManager.CPKRepatcher
{
    //Intended only for OE bgm/se.cpk
    internal static class CPKPatcher
    {

        public static async Task RepackDictionary(Game game, Dictionary<string, List<string>> cpkDict)
        {
            if (cpkDict == null || cpkDict.Count <= 0)
                return;

            Console.WriteLine("Repacking CPK's...");

            string cpkPath = Path.Combine(GamePath.GetModsPath(), "Parless");

            if (!Directory.Exists(cpkPath))
                Directory.CreateDirectory(cpkPath);

            foreach(var kv in cpkDict)
            {
                string cpkDir = cpkPath + kv.Key;
                string origCpk = GamePath.GetDataPath(game) + kv.Key + ".cpk";

                if (!Directory.Exists(cpkDir))
                    Directory.CreateDirectory(cpkDir);

                foreach(string mod in kv.Value)
                {
                    string modCpkDir = Path.Combine(GamePath.GetModsPath(), mod) + kv.Key;
                    string[] cpkFiles = Directory.GetFiles(modCpkDir, "*.");

                    foreach(string str in cpkFiles)
                        File.Copy(str, Path.Combine(cpkDir, Path.GetFileName(str)) , true);
                }

                CriPakTools.Program.Modify(origCpk, cpkDir, new DirectoryInfo(cpkDir).FullName + ".cpk");
            }


            await Task.CompletedTask;
        }
    }
}
