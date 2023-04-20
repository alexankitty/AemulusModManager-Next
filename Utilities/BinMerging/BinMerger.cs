﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Shapes;
using AemulusModManager.Utilities;
using AemulusModManager.Utilities.FileMerging;
using Pri.LongPath;
using Directory = Pri.LongPath.Directory;
using File = Pri.LongPath.File;
using Path = Pri.LongPath.Path;

namespace AemulusModManager
{
    public static class binMerge
    {
        private static string exePath = $@"{Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)}\Dependencies\PAKPack\PAKPack.exe";
        private static string[] looseExtensions = {".bin", ".abin", ".fpc", ".gsd", ".tpc", 
                                                  ".arc",".pac", ".pak", ".pack", ".spd" };
        // Use PAKPack command
        public static void PAKPackCMD(string args)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.CreateNoWindow = true;
            startInfo.UseShellExecute = false;
            startInfo.FileName = $"\"{exePath}\"";
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.Arguments = args;
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.Start();

                // Add this: wait until process does its work
                process.WaitForExit();
            }
        }

        public static List<string> getFileContents(string path)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.CreateNoWindow = true;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.FileName = $"\"{exePath}\"";
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.Arguments = $"list \"{path}\"";
            List<string> contents = new List<string>();
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.Start();
                while (!process.StandardOutput.EndOfStream)
                {
                    string line = process.StandardOutput.ReadLine();
                    if (!line.Contains(" "))
                    {
                        contents.Add(line);
                    }
                }
                // Add this: wait until process does its work
                process.WaitForExit();
            }
            return contents;
        }
        private static int commonPrefixUtil(String str1, String str2)
        {
            String result = "";
            int n1 = str1.Length,
                n2 = str2.Length;

            // Compare str1 and str2  
            for (int i = 0, j = 0;
                     i <= n1 - 1 && j <= n2 - 1;
                     i++, j++)
            {
                if (!str1[i].ToString().Equals(str2[j].ToString(), StringComparison.InvariantCultureIgnoreCase))
                {
                    break;
                }
                result += str1[i];
            }

            return result.Length;
        }

        private static List<string> getModList(string dir)
        {
            List<string> mods = new List<string>();
            string line;
            string[] list = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly)
                    .Where(s => (Path.GetFileName(s) == "mods.aem")).ToArray();
            if (list.Length > 0)
            {
                using (StreamReader stream = new StreamReader(list[0]))
                {
                    while ((line = stream.ReadLine()) != null)
                    {
                        mods.Add(line);
                    }
                }
            }
            return mods;
        }
        public static void DeleteDirectory(string path)
        {
            try
            {
                // Delete the contents of the directory
                foreach (string fileSystemEntry in Directory.EnumerateFileSystemEntries(path))
                {
                    if (File.Exists(fileSystemEntry))
                    {
                        File.Delete(fileSystemEntry);
                    }
                    else if (Directory.Exists(fileSystemEntry))
                    {
                        Directory.Delete(fileSystemEntry, true);
                    }
                }

                // Delete the directory
                Directory.Delete(path);
            }
            catch (Exception ex)
            {
                Utilities.ParallelLogger.Log("An error occurred: " + ex.Message);
            }
        
    }

        public static void Unpack(List<string> ModList, string modDir, bool useCpk, string cpkLang, string game)
        {
            if (!FileIOWrapper.Exists(exePath))
            {
                Utilities.ParallelLogger.Log($"[ERROR] Couldn't find {exePath}. Please check if it was blocked by your anti-virus.");
                return;
            }
            Utilities.ParallelLogger.Log("[INFO] Beginning to unpack...");
            // Copy over base PATCH1 file
            if (game == "Persona 5 Royal (Switch)")
            {
                if (FileIOWrapper.Exists($@"{Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)}\Original\{game}\PATCH1\MOVIE\MOV000.USM"))
                {
                    Utilities.ParallelLogger.Log($"[INFO] Copying over base PATCH1 file");
                    Directory.CreateDirectory($@"{modDir}\PATCH1\MOVIE");
                    FileIOWrapper.Copy($@"{Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)}\Original\{game}\PATCH1\MOVIE\MOV000.USM", $@"{modDir}\PATCH1\MOVIE\MOV000.USM", true);
                }
                else
                    Utilities.ParallelLogger.Log($@"[WARNING] {Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)}\Original\{game}\PATCH1\MOVIE\MOV000.USM not found, try unpacking base files again");
            }
            foreach (var mod in ModList)
            {
                if (!Directory.Exists(mod))
                {
                    Utilities.ParallelLogger.Log($"[ERROR] Cannot find {mod}");
                    continue;
                }

                // Run prebuild.bat
                if (FileIOWrapper.Exists($@"{mod}\prebuild.bat") && new Pri.LongPath.FileInfo($@"{mod}\prebuild.bat").Length > 0)
                {
                    Utilities.ParallelLogger.Log($@"[INFO] Running {mod}\prebuild.bat...");

                    ProcessStartInfo ProcessInfo;

                    ProcessInfo = new ProcessStartInfo();
                    ProcessInfo.FileName = Path.GetFullPath($@"{mod}\prebuild.bat");
                    ProcessInfo.CreateNoWindow = true;
                    ProcessInfo.UseShellExecute = false;
                    ProcessInfo.WorkingDirectory = Path.GetFullPath(mod);

                    using (Process process = new Process())
                    {
                        process.StartInfo = ProcessInfo;
                        process.Start();
                        process.WaitForExit();
                    }

                    Utilities.ParallelLogger.Log($@"[INFO] Finished running {mod}\prebuild.bat!");
                }

                List<string> modList = getModList(mod);

                string[] AemIgnore = FileIOWrapper.Exists($@"{mod}\Ignore.aem") ? FileIOWrapper.ReadAllLines($@"{mod}\Ignore.aem") : null;
                // Copy and overwrite everything thats not a bin
                foreach (var file in Directory.GetFiles(mod, "*", SearchOption.AllDirectories))
                {
                    // Copy everything except mods.aem and tblpatch to directory
                    if (Path.GetExtension(file).ToLower() != ".aem" && Path.GetExtension(file).ToLower() != ".tblpatch"
                        && Path.GetExtension(file).ToLower() != ".xml" && Path.GetExtension(file).ToLower() != ".png"
                        && Path.GetExtension(file).ToLower() != ".jpg" && Path.GetExtension(file).ToLower() != ".7z"
                        && Path.GetExtension(file).ToLower() != ".bat" && Path.GetExtension(file).ToLower() != ".txt"
                        && Path.GetExtension(file).ToLower() != ".zip" && Path.GetExtension(file).ToLower() != ".json"
                        && Path.GetExtension(file).ToLower() != ".tbp" && Path.GetExtension(file).ToLower() != ".rar"
                        && Path.GetExtension(file).ToLower() != ".exe" && Path.GetExtension(file).ToLower() != ".dll"
                        && Path.GetExtension(file).ToLower() != ".flow" && Path.GetExtension(file).ToLower() != ".msg"
                        && Path.GetExtension(file).ToLower() != ".back" && Path.GetExtension(file).ToLower() != ".bp"
                        && !Path.GetDirectoryName(file).Contains("spdpatches") && Path.GetExtension(file).ToLower() != ".pnach" 
                        && Path.GetFileNameWithoutExtension(file).ToLower() != "preview" 
                        && !file.Substring(mod.Length).ToLower().Contains("\\texture_override\\") //check if the file is in texture_override folder
                        && !(game == "Persona 3 Portable" && file.Substring(mod.Length).ToLower().Contains("\\fmv\\")) //check if the file is an FMV for P3P
                        && !((game == "Persona 3 Portable" || game == "Persona 1 (PSP)") && file.Substring(mod.Length).ToLower().Contains("\\cheats\\")))
                    {
                        List<string> folders = new List<string>(file.Split(char.Parse("\\")));
                        int idx = folders.IndexOf(Path.GetFileName(mod));
                        folders = folders.Skip(idx + 1).ToList();
                        string binPath = $@"{modDir}\{string.Join("\\", folders.ToArray())}";
                        string ogBinPath = $@"{Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)}\Original\{game}\{string.Join("\\", folders.ToArray())}";

                        if (AemIgnore != null && AemIgnore.Any(file.Contains))
                            continue;
                        else if (game != "Persona 1 (PSP)" 
                            && (Path.GetExtension(file).ToLower() == ".bin"
                            || Path.GetExtension(file).ToLower() == ".abin"
                            || Path.GetExtension(file).ToLower() == ".fpc"
                            || Path.GetExtension(file).ToLower() == ".arc"
                            || Path.GetExtension(file).ToLower() == ".pak"
                            || Path.GetExtension(file).ToLower() == ".pac"
                            || Path.GetExtension(file).ToLower() == ".pack"
                            || Path.GetExtension(file).ToLower() == ".gsd"
                            || Path.GetExtension(file).ToLower() == ".tpc"))
                        {
                            if (FileIOWrapper.Exists(ogBinPath) && modList.Count > 0)
                            {
                                // Check if mods.aem contains the modified parts of a bin
                                if (!modList.Exists(x => x.Contains($@"{Path.GetDirectoryName(string.Join("\\", folders.ToArray()))}\{Path.GetFileNameWithoutExtension(binPath)}\")))
                                {
                                    Utilities.ParallelLogger.Log($"[WARNING] Using {binPath} as base since nothing was specified in mods.aem");
                                    if (useCpk)
                                        binPath = Regex.Replace(binPath, "data0000[0-6]", Path.GetFileNameWithoutExtension(cpkLang));
                                    Directory.CreateDirectory(Path.GetDirectoryName(binPath));
                                    FileIOWrapper.Copy(file, binPath, true);
                                    continue;
                                }

                                Utilities.ParallelLogger.Log($@"[INFO] Unpacking {file}...");
                                // Unpack and transfer modified parts if base already exists
                                PAKPackCMD($"unpack \"{file}\"");
                                // Unpack fully before comparing to mods.aem
                                foreach (var f in Directory.GetFiles(Path.ChangeExtension(file, null), "*", SearchOption.AllDirectories))
                                {
                                    if (Path.GetExtension(f).ToLower() == ".bin"
                                    || Path.GetExtension(f).ToLower() == ".abin"
                                    || Path.GetExtension(f).ToLower() == ".fpc"
                                    || Path.GetExtension(f).ToLower() == ".arc"
                                    || Path.GetExtension(f).ToLower() == ".pak"
                                    || Path.GetExtension(f).ToLower() == ".pac"
                                    || Path.GetExtension(f).ToLower() == ".pack"
                                    || Path.GetExtension(f).ToLower() == ".gsd"
                                    || Path.GetExtension(f).ToLower() == ".tpc")
                                    {
                                        Utilities.ParallelLogger.Log($@"[INFO] Unpacking {f}...");
                                        PAKPackCMD($"unpack \"{f}\"");
                                        foreach (var f2 in Directory.GetFiles(Path.ChangeExtension(f, null), "*", SearchOption.AllDirectories))
                                        {
                                            if (Path.GetExtension(f2).ToLower() == ".bin"
                                            || Path.GetExtension(f2).ToLower() == ".abin"
                                            || Path.GetExtension(f2).ToLower() == ".fpc"
                                            || Path.GetExtension(f2).ToLower() == ".arc"
                                            || Path.GetExtension(f2).ToLower() == ".pak"
                                            || Path.GetExtension(f2).ToLower() == ".pac"
                                            || Path.GetExtension(f2).ToLower() == ".pack"
                                            || Path.GetExtension(f2).ToLower() == ".gsd"
                                            || Path.GetExtension(f2).ToLower() == ".tpc")
                                            {
                                                Utilities.ParallelLogger.Log($@"[INFO] Unpacking {f2}...");
                                                PAKPackCMD($"unpack \"{f2}\"");
                                            }
                                            else if (Path.GetExtension(f2).ToLower() == ".spd")
                                            {
                                                Utilities.ParallelLogger.Log($@"[INFO] Unpacking {f2}...");
                                                Directory.CreateDirectory(Path.ChangeExtension(f2, null));
                                                List<DDS> ddsFiles = spdUtils.getDDSFiles(f2);
                                                foreach (var ddsFile in ddsFiles)
                                                {
                                                    string spdFolder = Path.ChangeExtension(f2, null);
                                                    FileIOWrapper.WriteAllBytes($@"{spdFolder}\{ddsFile.name}.dds", ddsFile.file);
                                                }
                                                List<SPDKey> spdKeys = spdUtils.getSPDKeys(f2);
                                                foreach (var spdKey in spdKeys)
                                                {
                                                    string spdFolder = Path.ChangeExtension(f2, null);
                                                    FileIOWrapper.WriteAllBytes($@"{spdFolder}\{spdKey.id}.spdspr", spdKey.file);
                                                }
                                            }
                                            else if (Path.GetExtension(f2) == ".spr" && game != "Persona Q2")
                                            {
                                                Utilities.ParallelLogger.Log($@"[INFO] Unpacking {f2}...");
                                                string sprFolder2 = Path.ChangeExtension(f2, null);
                                                Directory.CreateDirectory(sprFolder2);
                                                Dictionary<string, int> tmxNames = sprUtils.getTmxNames(f2);
                                                foreach (string name in tmxNames.Keys)
                                                {
                                                    byte[] tmx = sprUtils.extractTmx(f2, name);
                                                    FileIOWrapper.WriteAllBytes($@"{sprFolder2}\{name}.tmx", tmx);
                                                }
                                            }
                                        }
                                    }
                                    else if (Path.GetExtension(f).ToLower() == ".spd")
                                    {
                                        Directory.CreateDirectory(Path.ChangeExtension(f, null));
                                        List<DDS> ddsFiles = spdUtils.getDDSFiles(f);
                                        foreach (var ddsFile in ddsFiles)
                                        {
                                            string spdFolder = Path.ChangeExtension(f, null);
                                            FileIOWrapper.WriteAllBytes($@"{spdFolder}\{ddsFile.name}.dds", ddsFile.file);
                                        }
                                        List<SPDKey> spdKeys = spdUtils.getSPDKeys(f);
                                        foreach (var spdKey in spdKeys)
                                        {
                                            string spdFolder = Path.ChangeExtension(f, null);
                                            FileIOWrapper.WriteAllBytes($@"{spdFolder}\{spdKey.id}.spdspr", spdKey.file);
                                        }
                                    }
                                    else if (Path.GetExtension(f) == ".spr" && game != "Persona Q2")
                                    {
                                        Utilities.ParallelLogger.Log($@"[INFO] Unpacking {f}...");
                                        string sprFolder = Path.ChangeExtension(f, null);
                                        Directory.CreateDirectory(sprFolder);
                                        Dictionary<string, int> tmxNames = sprUtils.getTmxNames(f);
                                        foreach (string name in tmxNames.Keys)
                                        {
                                            byte[] tmx = sprUtils.extractTmx(f, name);
                                            FileIOWrapper.WriteAllBytes($@"{sprFolder}\{name}.tmx", tmx);
                                        }
                                    }

                                }
                            }
                            else
                            {
                                if (useCpk)
                                {
                                    binPath = Regex.Replace(binPath, "data0000[0-6]", Path.GetFileNameWithoutExtension(cpkLang));
                                    binPath = Regex.Replace(binPath, "movie0000[0-2]", "movie");
                                }
                                Directory.CreateDirectory(Path.GetDirectoryName(binPath));
                                FileIOWrapper.Copy(file, binPath, true);
                                Utilities.ParallelLogger.Log($"[INFO] Copying over {file} to {binPath}");
                            }
                        }
                        else if (game != "Persona 1 (PSP)" && Path.GetExtension(file).ToLower() == ".spd")
                        {
                            if (FileIOWrapper.Exists(ogBinPath) && modList.Count > 0)
                            {
                                Utilities.ParallelLogger.Log($@"[INFO] Unpacking {file}...");
                                Directory.CreateDirectory(Path.ChangeExtension(file, null));
                                List<DDS> ddsFiles = spdUtils.getDDSFiles(file);
                                foreach (var ddsFile in ddsFiles)
                                {
                                    string spdFolder = Path.ChangeExtension(file, null);
                                    FileIOWrapper.WriteAllBytes($@"{spdFolder}\{ddsFile.name}.dds", ddsFile.file);
                                }
                                List<SPDKey> spdKeys = spdUtils.getSPDKeys(file);
                                foreach (var spdKey in spdKeys)
                                {
                                    string spdFolder = Path.ChangeExtension(file, null);
                                    FileIOWrapper.WriteAllBytes($@"{spdFolder}\{spdKey.id}.spdspr", spdKey.file);
                                }
                            }
                            else
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(binPath));
                                FileIOWrapper.Copy(file, binPath, true);
                                Utilities.ParallelLogger.Log($"[INFO] Copying over {file} to {binPath}");
                            }
                        }
                        else
                        {
                            if (useCpk)
                            {
                                binPath = Regex.Replace(binPath, "data0000[0-6]", Path.GetFileNameWithoutExtension(cpkLang));
                                binPath = Regex.Replace(binPath, "movie0000[0-2]", "movie");
                            }
                            Directory.CreateDirectory(Path.GetDirectoryName(binPath));
                            FileIOWrapper.Copy(file, binPath, true);
                            Utilities.ParallelLogger.Log($"[INFO] Copying over {file} to {binPath}");
                        }
                    }
                }

                // Copy over loose files specified by mods.aem
                foreach (var m in modList)
                {
                    if (FileIOWrapper.Exists($@"{mod}\{m}"))
                    {
                        string dir = $@"{modDir}\{m}";
                        if (useCpk)
                        {
                            dir = Regex.Replace(dir, "data0000[0-6]", Path.GetFileNameWithoutExtension(cpkLang));
                            dir = Regex.Replace(dir, "movie0000[0-2]", "movie");
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(dir));
                        FileIOWrapper.Copy($@"{mod}\{m}", dir, true);
                        Utilities.ParallelLogger.Log($@"[INFO] Copying over {mod}\{m} as specified by mods.aem");
                    }
                }
                if (game != "Persona 1 (PSP)")
                {
                    // Go through mod directory again to delete unpacked files after bringing them in
                    foreach (var file in Directory.GetFiles(mod, "*", SearchOption.AllDirectories))
                    {
                        if ((Path.GetExtension(file).ToLower() == ".bin"
                            || Path.GetExtension(file).ToLower() == ".abin"
                            || Path.GetExtension(file).ToLower() == ".fpc"
                            || Path.GetExtension(file).ToLower() == ".arc"
                            || Path.GetExtension(file).ToLower() == ".pak"
                            || Path.GetExtension(file).ToLower() == ".pac"
                            || Path.GetExtension(file).ToLower() == ".pack"
                            || Path.GetExtension(file).ToLower() == ".gsd"
                            || Path.GetExtension(file).ToLower() == ".tpc"
                            || Path.GetExtension(file).ToLower() == ".spd"
                            || Path.GetExtension(file).ToLower() == ".spr")
                            && Directory.Exists(Path.ChangeExtension(file, null))
                            && Path.GetFileName(Path.ChangeExtension(file, null)) != "result"
                            && Path.GetFileName(Path.ChangeExtension(file, null)) != "panel"
                            && Path.GetFileName(Path.ChangeExtension(file, null)) != "crossword")
                        {
                            DeleteDirectory(Path.ChangeExtension(file, null));
                        }
                    }

                    if (FileIOWrapper.Exists($@"{mod}\battle\result.pac") && Directory.Exists($@"{mod}\battle\result\result"))
                    {
                        foreach (var f in Directory.GetFiles($@"{mod}\battle\result\result"))
                        {
                            if (Path.GetExtension(f).ToLower() == ".gfs" || Path.GetExtension(f).ToLower() == ".gmd")
                                FileIOWrapper.Delete(f);
                        }
                    }
                    if (FileIOWrapper.Exists($@"{mod}\battle\result\result.spd") && Directory.Exists($@"{mod}\battle\result\result"))
                    {
                        foreach (var f in Directory.GetFiles($@"{mod}\battle\result\result"))
                        {
                            if (Path.GetExtension(f).ToLower() == ".dds" || Path.GetExtension(f).ToLower() == ".spdspr")
                                FileIOWrapper.Delete(f);
                        }
                    }
                    if (FileIOWrapper.Exists($@"{mod}\field\panel.bin") && Directory.Exists($@"{mod}\field\panel\panel"))
                        DeleteDirectory($@"{mod}\field\panel\panel");
                    if (Directory.Exists($@"{mod}\battle\result\result") && !Directory.GetFiles($@"{mod}\battle\result\result", "*", SearchOption.AllDirectories).Any())
                        DeleteDirectory($@"{mod}\battle\result\result");
                    if (Directory.Exists($@"{mod}\battle\result") && !Directory.GetFiles($@"{mod}\battle\result", "*", SearchOption.AllDirectories).Any())
                        DeleteDirectory($@"{mod}\battle\result");
                    if (Directory.Exists($@"{mod}\field\panel") && !Directory.EnumerateFileSystemEntries($@"{mod}\field\panel").Any())
                        DeleteDirectory($@"{mod}\field\panel");
                    if ((FileIOWrapper.Exists($@"{mod}\minigame\crossword.pak") || FileIOWrapper.Exists($@"{mod}\minigame\crossword.spd")) && Directory.Exists($@"{mod}\minigame\crossword"))
                    {
                        foreach (var f in Directory.GetFiles($@"{mod}\minigame\crossword"))
                        {
                            if (Path.GetExtension(f).ToLower() != ".pak")
                                FileIOWrapper.Delete(f);
                        }
                    }
                    if (Directory.Exists($@"{mod}\minigame\crossword") && !Directory.GetFiles($@"{mod}\minigame\crossword", "*", SearchOption.AllDirectories).Any())
                        DeleteDirectory($@"{mod}\minigame\crossword");
                }
            }
            Utilities.ParallelLogger.Log("[INFO] Finished unpacking!");
        }
        public static bool FileExistsCaseSensitive(string filename)
        {
            string name = Path.GetDirectoryName(filename);
            return name != null
                   && Array.Exists(Directory.GetFiles(name), s => s == Path.GetFullPath(filename));
        }
        private static void ProcessLoose(string ogPath, string d)
        {
            if (!Directory.Exists(Path.GetDirectoryName(d)))
                Directory.CreateDirectory(Path.GetDirectoryName(d));
            if (Path.GetFileName(ogPath) == "panel.bin")
            {
                if (!Directory.Exists($@"{d}\panel"))
                    return;
            }
            if (Path.GetFileName(ogPath) == "result.pac")
            {
                if (!Directory.Exists($@"{d}\result"))
                    return;
                if (!Directory.GetFiles($@"{d}\result", "*.GFS", SearchOption.TopDirectoryOnly).Any()
                    && !Directory.GetFiles($@"{d}\result", "*.GMD", SearchOption.TopDirectoryOnly).Any())
                    return;
            }
            if (Path.GetFileName(ogPath) == "crossword.pak")
            {
                if (!Directory.GetFiles(d, "*.dds", SearchOption.AllDirectories).Any()
                    && !Directory.GetFiles(d, "*.spdspr", SearchOption.AllDirectories).Any()
                    && !Directory.GetFiles(d, "*.bmd", SearchOption.AllDirectories).Any()
                    && !Directory.GetFiles(d, "*.plg", SearchOption.AllDirectories).Any())
                    return;
            }
            if (Path.GetFileName(ogPath) == "result.spd")
            {
                if (!Directory.GetFiles(d, "*.dds", SearchOption.TopDirectoryOnly).Any()
                    && !Directory.GetFiles(d, "*.spdspr", SearchOption.TopDirectoryOnly).Any())
                    return;
            }
            if (Path.GetFileName(ogPath) == "crossword.spd")
            {
                if (!Directory.GetFiles(d, "*.dds", SearchOption.TopDirectoryOnly).Any()
                    && !Directory.GetFiles(d, "*.spdspr", SearchOption.TopDirectoryOnly).Any())
                    return;
            }
            Utilities.ParallelLogger.Log($"[INFO] Copying over {ogPath} to use as base.");
            FileIOWrapper.Copy(ogPath, $@"{Path.GetDirectoryName(d)}\{Path.GetFileName(ogPath)}");
        }

        public static void Merge(string modDir, string game)
        {
            Utilities.ParallelLogger.Log("[INFO] Beginning to merge...");
            // Check if loose folder matches vanilla bin file
            foreach (var d in Directory.GetDirectories(modDir, "*", SearchOption.AllDirectories))
            {
                List<string> folders = new List<string>(d.Split(char.Parse("\\")));
                int idx = folders.IndexOf(Path.GetFileName(modDir)) + 1;
                folders = folders.Skip(idx).ToList();
                string ogPath = $@"{Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)}\Original\{game}\{string.Join("\\", folders.ToArray())}";
                for( int i = 0; i < looseExtensions.Length; i++)
                {
                    string extension = looseExtensions[i];
                    string extensionUpper = looseExtensions[i].ToUpper();
                    if (FileExistsCaseSensitive(ogPath + extension) && !FileExistsCaseSensitive(d + extension))
                    {
                        ProcessLoose(ogPath + extension, d);
                    }
                    
                    if (FileExistsCaseSensitive(ogPath + extensionUpper) && !FileExistsCaseSensitive(d + extensionUpper))
                    {
                        ProcessLoose(ogPath + extensionUpper, d);
                    }
                }
            }

            foreach (var file in Directory.GetFiles(modDir, "*", SearchOption.AllDirectories))
            {
                if (Path.GetExtension(file).ToLower() == ".bin"
                    || Path.GetExtension(file).ToLower() == ".abin"
                    || Path.GetExtension(file).ToLower() == ".fpc"
                    || Path.GetExtension(file).ToLower() == ".arc"
                    || Path.GetExtension(file).ToLower() == ".pak"
                    || Path.GetExtension(file).ToLower() == ".pac"
                    || Path.GetExtension(file).ToLower() == ".pack"
                    || Path.GetExtension(file).ToLower() == ".gsd"
                    || Path.GetExtension(file).ToLower() == ".tpc")
                {
                    if (Directory.Exists(Path.ChangeExtension(file, null)))
                    {
                        Utilities.ParallelLogger.Log($@"[INFO] Merging {file}...");
                        string bin = file;
                        string binFolder = Path.ChangeExtension(file, null);

                        // Get contents of init_free
                        List<string> contents = getFileContents(bin);

                        // Unpack archive for future unpacking
                        string temp = $"{binFolder}_temp";
                        PAKPackCMD($"unpack \"{bin}\" \"{temp}\"");

                        foreach (var f in Directory.GetFiles(binFolder, "*", SearchOption.AllDirectories))
                        {
                            // Get bin path used for PAKPack.exe
                            int numParFolders = Path.ChangeExtension(file, null).Split(char.Parse("\\")).Length;
                            List<string> folders = new List<string>(f.Split(char.Parse("\\")));
                            string binPath = string.Join("/", folders.ToArray().Skip(numParFolders).ToArray());
                            // Case for paths in Persona 5 event paks
                            if (contents.Contains($"../../../{binPath}"))
                            {
                                string args = $"replace \"{bin}\" ../../../{binPath} \"{f}\" \"{bin}\"";
                                PAKPackCMD(args);
                            }
                            else if (contents.Contains($"../../{binPath}"))
                            {
                                string args = $"replace \"{bin}\" ../../{binPath} \"{f}\" \"{bin}\"";
                                PAKPackCMD(args);
                            }
                            else if (contents.Contains($"../{binPath}"))
                            {
                                string args = $"replace \"{bin}\" ../{binPath} \"{f}\" \"{bin}\"";
                                PAKPackCMD(args);
                            }
                            // Check if more unpacking needs to be done to replace
                            else if (!contents.Contains(binPath))
                            {
                                string longestPrefix = "";
                                int longestPrefixLen = 0;
                                foreach (var c in contents)
                                {
                                    int prefixLen = commonPrefixUtil(c, binPath);
                                    int otherPrefixLen = commonPrefixUtil(c, $"../../{binPath}");
                                    int otherOtherPrefixLen = commonPrefixUtil(c, $"../{binPath}");
                                    int maxLen = Math.Max(Math.Max(prefixLen, otherPrefixLen), otherOtherPrefixLen);
                                    if (maxLen > longestPrefixLen)
                                    {
                                        longestPrefix = c;
                                        longestPrefixLen = maxLen;
                                    }
                                    else if (maxLen == longestPrefixLen)
                                    {
                                        if ((Path.GetExtension(c).Equals(".spd", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(c).Equals(".bin", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(c).Equals(".abin", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(c).Equals(".fpc", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(c).Equals(".gsd", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(c).Equals(".tpc", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(c).Equals(".arc", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(c).Equals(".pac", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(c).Equals(".pack", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(c).Equals(".pak", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(c).Equals(".spr", StringComparison.InvariantCultureIgnoreCase))
                                            && !(Path.GetExtension(longestPrefix).Equals(".spd", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(longestPrefix).Equals(".bin", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(longestPrefix).Equals(".abin", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(longestPrefix).Equals(".fpc", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(longestPrefix).Equals(".gsd", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(longestPrefix).Equals(".tpc", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(longestPrefix).Equals(".arc", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(longestPrefix).Equals(".pac", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(longestPrefix).Equals(".pack", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(longestPrefix).Equals(".pak", StringComparison.InvariantCultureIgnoreCase)
                                            || Path.GetExtension(longestPrefix).Equals(".spr", StringComparison.InvariantCultureIgnoreCase)))
                                            longestPrefix = c;
                                        else if (c[longestPrefixLen] == '.')
                                            longestPrefix = c;
                                    }
                                }
                                // Check if we can unpack again
                                if (Path.GetExtension(longestPrefix).ToLower() == ".bin"
                                || Path.GetExtension(longestPrefix).ToLower() == ".abin"
                                || Path.GetExtension(longestPrefix).ToLower() == ".fpc"
                                || Path.GetExtension(longestPrefix).ToLower() == ".arc"
                                || Path.GetExtension(longestPrefix).ToLower() == ".pak"
                                || Path.GetExtension(longestPrefix).ToLower() == ".gsd"
                                || Path.GetExtension(longestPrefix).ToLower() == ".tpc"
                                || Path.GetExtension(longestPrefix).ToLower() == ".pac"
                                || Path.GetExtension(longestPrefix).ToLower() == ".pack")
                                {
                                    string file2 = $@"{temp}\{longestPrefix.Replace("/", "\\")}";
                                    List<string> contents2 = getFileContents(file2);

                                    List<string> split = new List<string>(binPath.Split(char.Parse("/")));
                                    int numPrefixFolders = longestPrefix.Split(char.Parse("/")).Length;
                                    string binPath2 = string.Join("/", split.ToArray().Skip(numPrefixFolders).ToArray());

                                    if (contents2.Contains(binPath2))
                                    {
                                        PAKPackCMD($"replace \"{file2}\" {binPath2} \"{f}\" \"{file2}\"");
                                        PAKPackCMD($"replace \"{bin}\" {longestPrefix} \"{file2}\" \"{bin}\"");
                                    }
                                    else
                                    {
                                        string longestPrefix2 = "";
                                        int longestPrefixLen2 = 0;
                                        foreach (var c in contents2)
                                        {
                                            int prefixLen = commonPrefixUtil(c, binPath2);
                                            if (prefixLen > longestPrefixLen2)
                                            {
                                                longestPrefix2 = c;
                                                longestPrefixLen2 = prefixLen;
                                            }
                                        }
                                        // Check if we can unpack again
                                        if (Path.GetExtension(longestPrefix2).ToLower() == ".bin"
                                        || Path.GetExtension(longestPrefix2).ToLower() == ".abin"
                                        || Path.GetExtension(longestPrefix2).ToLower() == ".fpc"
                                        || Path.GetExtension(longestPrefix2).ToLower() == ".gsd"
                                        || Path.GetExtension(longestPrefix2).ToLower() == ".tpc"
                                        || Path.GetExtension(longestPrefix2).ToLower() == ".arc"
                                        || Path.GetExtension(longestPrefix2).ToLower() == ".pak"
                                        || Path.GetExtension(longestPrefix2).ToLower() == ".pac"
                                        || Path.GetExtension(longestPrefix2).ToLower() == ".pack")
                                        {
                                            string file3 = $@"{temp}\{Path.ChangeExtension(longestPrefix.Replace("/", "\\"), null)}\{longestPrefix2.Replace("/", "\\")}";
                                            PAKPackCMD($"unpack \"{file2}\"");
                                            List<string> contents3 = getFileContents(file3);

                                            List<string> split2 = new List<string>(binPath2.Split(char.Parse("/")));
                                            int numPrefixFolders2 = longestPrefix2.Split(char.Parse("/")).Length;
                                            string binPath3 = string.Join("/", split2.ToArray().Skip(numPrefixFolders2).ToArray());

                                            if (contents3.Contains(binPath3))
                                            {
                                                PAKPackCMD($"replace \"{file3}\" {binPath3} \"{f}\" \"{file3}\"");
                                                PAKPackCMD($"replace \"{file2}\" {longestPrefix2} \"{file3}\" \"{file2}\"");
                                                PAKPackCMD($"replace \"{bin}\" {longestPrefix} \"{file2}\" \"{bin}\"");
                                            }
                                        }
                                        else if (Path.GetExtension(longestPrefix2).ToLower() == ".spd" && (Path.GetExtension(f).ToLower() == ".dds" || Path.GetExtension(f).ToLower() == ".spdspr"))
                                        {
                                            PAKPackCMD($"unpack \"{file2}\"");
                                            string spdPath = $@"{temp}\{Path.ChangeExtension(longestPrefix.Replace("/", "\\"), null)}\{longestPrefix2.Replace("/", "\\")}";                                          
                                            if (Path.GetExtension(f).ToLower() == ".dds")
                                                spdUtils.replaceDDS(spdPath, f);
                                            else
                                                spdUtils.replaceSPDKey(spdPath, f);
                                            Utilities.ParallelLogger.Log($"[INFO] Replacing {spdPath} in {f}");
                                            PAKPackCMD($"replace \"{file2}\" {longestPrefix2} \"{spdPath}\" \"{file2}\"");
                                            PAKPackCMD($"replace \"{bin}\" {longestPrefix} \"{file2}\" \"{bin}\"");
                                        }
                                        else if (game != "Persona Q2" && Path.GetExtension(longestPrefix2).ToLower() == ".spr" && Path.GetExtension(f).ToLower() == ".tmx")
                                        {
                                            PAKPackCMD($"unpack \"{file2}\"");
                                            string sprPath = $@"{temp}\{Path.ChangeExtension(longestPrefix.Replace("/", "\\"), null)}\{longestPrefix2.Replace("/", "\\")}";
                                            sprUtils.replaceTmx(sprPath, f);
                                            PAKPackCMD($"replace \"{file2}\" {longestPrefix2} \"{sprPath}\" \"{file2}\"");
                                            PAKPackCMD($"replace \"{bin}\" {longestPrefix} \"{file2}\" \"{bin}\"");
                                        }
                                    }
                                }
                                else if (Path.GetExtension(longestPrefix).ToLower() == ".spd" && (Path.GetExtension(f).ToLower() == ".dds" || Path.GetExtension(f).ToLower() == ".spdspr"))
                                {
                                    string spdPath = $@"{temp}\{longestPrefix.Replace("/", "\\")}";
                                    if (FileIOWrapper.Exists(spdPath.Replace("_temp", String.Empty)))
                                        FileIOWrapper.Copy(spdPath.Replace("_temp", String.Empty), spdPath, true);
                                    if (Path.GetExtension(f).ToLower() == ".dds")
                                        spdUtils.replaceDDS(spdPath, f);
                                    else
                                        spdUtils.replaceSPDKey(spdPath, f);
                                    Utilities.ParallelLogger.Log($"[INFO] Replacing {spdPath} in {f}");
                                    PAKPackCMD($"replace \"{bin}\" {longestPrefix} \"{spdPath}\" \"{bin}\"");
                                }
                                else if (Path.GetExtension(longestPrefix).ToLower() == ".spr" && Path.GetExtension(f).ToLower() == ".tmx")
                                {
                                    string path = longestPrefix.Replace("../", "");
                                    string sprPath = $@"{temp}\{path.Replace("/", "\\")}";
                                    sprUtils.replaceTmx(sprPath, f);
                                    PAKPackCMD($"replace \"{bin}\" {longestPrefix} \"{sprPath}\" \"{bin}\"");
                                }
                            }
                            else
                            {
                                string args = $"replace \"{bin}\" {binPath} \"{f}\" \"{bin}\"";
                                PAKPackCMD(args);
                            }
                        }
                        DeleteDirectory(temp);
                    }
                }
                else if (Path.GetExtension(file).ToLower() == ".spd")
                {
                    Utilities.ParallelLogger.Log($@"[INFO] Merging {file}...");
                    string spdFolder = Path.ChangeExtension(file, null);
                    if (Directory.Exists(spdFolder))
                    {
                        foreach (var spdFile in Directory.GetFiles(spdFolder, "*", SearchOption.AllDirectories))
                        {
                            if (Path.GetExtension(spdFile).ToLower() == ".dds")
                            {
                                Utilities.ParallelLogger.Log($"[INFO] Replacing {spdFile} in {file}");
                                spdUtils.replaceDDS(file, spdFile);
                            }
                            else if (Path.GetExtension(spdFile).ToLower() == ".spdspr")
                            {
                                spdUtils.replaceSPDKey(file, spdFile);
                                Utilities.ParallelLogger.Log($"[INFO] Replacing {spdFile} in {file}");
                            }
                        }
                    }
                }
                else if (game != "Persona Q2" && Path.GetExtension(file).ToLower() == ".spr")
                {
                    Utilities.ParallelLogger.Log($@"[INFO] Merging {file}...");
                    string sprFolder = Path.ChangeExtension(file, null);
                    if (Directory.Exists(sprFolder))
                    {
                        foreach (var sprFile in Directory.GetFiles(sprFolder, "*", SearchOption.AllDirectories))
                        {
                            Utilities.ParallelLogger.Log($"[INFO] Replacing {sprFile} in {file}");
                            sprUtils.replaceTmx(file, sprFile);
                        }
                    }
                }
            }
            
            // Go through mod directory again to delete unpacked files after bringing them in
            foreach (var file in Directory.GetFiles(modDir, "*", SearchOption.AllDirectories))
            {
                if ((Path.GetExtension(file).ToLower() == ".bin"
                    || Path.GetExtension(file).ToLower() == ".abin"
                    || Path.GetExtension(file).ToLower() == ".fpc"
                    || Path.GetExtension(file).ToLower() == ".gsd"
                    || Path.GetExtension(file).ToLower() == ".tpc"
                    || Path.GetExtension(file).ToLower() == ".arc"
                    || Path.GetExtension(file).ToLower() == ".pak"
                    || Path.GetExtension(file).ToLower() == ".pac"
                    || Path.GetExtension(file).ToLower() == ".pack"
                    || Path.GetExtension(file).ToLower() == ".spd"
                    || Path.GetExtension(file).ToLower() == ".spr")
                    && Directory.Exists(Path.ChangeExtension(file, null))
                    && Path.GetFileName(Path.ChangeExtension(file, null)) != "result"
                    && Path.GetFileName(Path.ChangeExtension(file, null)) != "panel"
                    && Path.GetFileName(Path.ChangeExtension(file, null)) != "crossword")
                {
                    DeleteDirectory(Path.ChangeExtension(file, null));
                }
            }

            // Hardcoded cases TODO: reimplement extracted folders to have file extensions as part of the name, although would need to refactor every aemulus mod

            if (FileIOWrapper.Exists($@"{modDir}\battle\result.pac") && !FileIOWrapper.Exists($@"{modDir}\battle\result\result.spd") && Directory.Exists($@"{modDir}\battle\result"))
                DeleteDirectory($@"{modDir}\battle\result");
            if (Directory.Exists($@"{modDir}\battle\result\result"))
                DeleteDirectory($@"{modDir}\battle\result\result");
            if (Directory.Exists($@"{modDir}\minigame\crossword\crossword"))
                DeleteDirectory($@"{modDir}\minigame\crossword\crossword");
            if (Directory.Exists($@"{modDir}\field\panel\panel"))
                DeleteDirectory($@"{modDir}\field\panel\panel");
            if (Directory.Exists($@"{modDir}\field\panel") && !Directory.EnumerateFileSystemEntries($@"{modDir}\field\panel").Any())
                DeleteDirectory($@"{modDir}\field\panel");

            if (Directory.Exists($@"{modDir}\minigame\crossword\crossword"))
                DeleteDirectory($@"{modDir}\minigame\crossword\crossword");
            if (Directory.Exists($@"{modDir}\minigame\crossword"))
            {
                foreach (var file in Directory.GetFiles($@"{modDir}\minigame\crossword", "*", SearchOption.AllDirectories))
                    if (Path.GetExtension(file).ToLower() != ".pak")
                        FileIOWrapper.Delete(file);
            }
            if (Directory.Exists($@"{modDir}\minigame\crossword") && !Directory.EnumerateFileSystemEntries($@"{modDir}\minigame\crossword").Any())
                DeleteDirectory($@"{modDir}\minigame\crossword");

            Utilities.ParallelLogger.Log("[INFO] Finished merging!");
            return;
        }

        public static void Restart(string modDir, bool emptySND, string game, string cpkLang, string cheats, string cheatsWS, bool empty = false)
        {
            Utilities.ParallelLogger.Log("[INFO] Deleting current mod build...");
            // Revert appended cpks
            if (game == "Persona 4 Golden")
            {
                string path = Path.GetDirectoryName(modDir);
                // Copy original cpk back if different
                if (FileIOWrapper.Exists($@"{Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)}\Original\Persona 4 Golden\{cpkLang}") && FileIOWrapper.Exists($@"{path}\{cpkLang}")
                    && GetChecksumString($@"{Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)}\Original\Persona 4 Golden\{cpkLang}") != GetChecksumString($@"{path}\{cpkLang}"))
                {
                    Utilities.ParallelLogger.Log($@"[INFO] Reverting {cpkLang} back to original");
                    FileIOWrapper.Copy($@"{Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)}\Original\Persona 4 Golden\{cpkLang}", $@"{path}\{cpkLang}", true);
                }
                // Copy original cpk back if different
                if (FileIOWrapper.Exists($@"{Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)}\Original\Persona 4 Golden\movie.cpk") && FileIOWrapper.Exists($@"{path}\{cpkLang}")
                    && GetChecksumString($@"{Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)}\Original\Persona 4 Golden\movie.cpk") != GetChecksumString($@"{path}\movie.cpk"))
                {
                    Utilities.ParallelLogger.Log($@"[INFO] Reverting movie.cpk back to original");
                    FileIOWrapper.Copy($@"{Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)}\Original\Persona 4 Golden\movie.cpk", $@"{path}\movie.cpk", true);
                }
                // Delete modified pacs
                if (FileIOWrapper.Exists($@"{path}\data00007.pac"))
                {
                    Utilities.ParallelLogger.Log($"[INFO] Deleting data00007.pac");
                    FileIOWrapper.Delete($@"{path}\data00007.pac");
                }
                if (FileIOWrapper.Exists($@"{path}\movie00003.pac"))
                {
                    Utilities.ParallelLogger.Log($"[INFO] Deleting movie00003.pac");
                    FileIOWrapper.Delete($@"{path}\movie00003.pac");
                }
            }

            if (!emptySND || game == "Persona 3 FES")
            {
                Utilities.ParallelLogger.Log("[INFO] Keeping SND folder.");
                foreach (var dir in Directory.GetDirectories(modDir))
                {
                    if (Path.GetFileName(dir).ToLower() != "snd")
                        DeleteDirectory(dir);
                }
                // Delete top layer files too
                foreach (var file in Directory.GetFiles(modDir))
                {
                    if (Path.GetExtension(file).ToLower() != ".elf" && Path.GetExtension(file).ToLower() != ".iso")
                        FileIOWrapper.Delete(file);
                }
            }
            else
            {
                if (Directory.Exists(modDir))
                    DeleteDirectory(modDir);
                Directory.CreateDirectory(modDir);
            }
            if (game == "Persona Q2" && empty)
            {
                File.Create($@"{modDir}\dummy.txt");
                MakeCpk(modDir, true, empty);
            }
            // Delete Aemulus pnaches in cheats folder
            if (game == "Persona 3 FES" && cheats != null && Directory.Exists(cheats))
            {
                foreach (var pnach in Directory.GetFiles(cheats, "*_aem.pnach", SearchOption.TopDirectoryOnly))
                    File.Delete(pnach);
            }
            // Delete Aemulus pnaches in cheats_ws folder
            if (game == "Persona 3 FES" && cheatsWS != null && Directory.Exists(cheatsWS))
            {
                foreach (var pnach in Directory.GetFiles(cheatsWS, "*_aem.pnach", SearchOption.TopDirectoryOnly))
                    File.Delete(pnach);
            }
        }

        public static string GetChecksumString(string filePath)
        {
            string checksumString = null;

            // get md5 checksum of file
            using (var md5 = MD5.Create())
            {
                using (var stream = FileIOWrapper.OpenRead(filePath))
                {
                    // get hash
                    byte[] currentFileSum = md5.ComputeHash(stream);
                    // convert hash to string
                    checksumString = BitConverter.ToString(currentFileSum).Replace("-", "");
                }
            }

            return checksumString;
        }

        public static void MakeCpk(string modDir, bool crc, bool empty = false)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.CreateNoWindow = true;
            startInfo.FileName = $@"{Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)}\Dependencies\CpkMakeC\cpkmakec.exe";
            if (!FileIOWrapper.Exists(startInfo.FileName))
            {
                Utilities.ParallelLogger.Log($"[ERROR] Couldn't find {startInfo.FileName}. Please check if it was blocked by your anti-virus.");
                return;
            }
            var extension = Path.GetFileName(modDir) == "PATCH1" ? "CPK" : "cpk";
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.Arguments = $"\"{modDir}\" \"{modDir}\".{extension} -mode=FILENAME";
            if (crc)
                startInfo.Arguments += " -crc";
            if (!empty)
                Utilities.ParallelLogger.Log($"[INFO] Building {Path.GetFileName(modDir)}.{extension}...");
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.Start();
                process.WaitForExit();
            }
        }

        public static void LoadCheats(List<string> mods, string cheatsDir)
        {
            foreach (string dir in mods)
            {
                Utilities.ParallelLogger.Log($"[INFO] Searching for cheats in {dir}...");
                if (!Directory.Exists($@"{dir}\cheats"))
                {
                    Utilities.ParallelLogger.Log($"[INFO] No cheats folder found in {dir}");
                    continue;
                }
                // Copy over cheats
                foreach (var cheat in Directory.GetFiles($@"{dir}\cheats", "*.pnach", SearchOption.AllDirectories))
                {
                    File.Copy(cheat, $@"{cheatsDir}\{Path.GetFileNameWithoutExtension(cheat)}_aem.pnach", true);
                    Utilities.ParallelLogger.Log($"[INFO] Copied over {Path.GetFileNameWithoutExtension(cheat)}_aem.pnach to {cheatsDir}");
                }
            }
        }
        public static void LoadCheatsWS(List<string> mods, string cheatsDir)
        {
            foreach (string dir in mods)
            {
                Utilities.ParallelLogger.Log($"[INFO] Searching for cheats_ws in {dir}...");
                if (!Directory.Exists($@"{dir}\cheats_ws"))
                {
                    Utilities.ParallelLogger.Log($"[INFO] No cheats_ws folder found in {dir}");
                    continue;
                }
                // Copy over cheats
                foreach (var cheat in Directory.GetFiles($@"{dir}\cheats_ws", "*.pnach", SearchOption.AllDirectories))
                {
                    File.Copy(cheat, $@"{cheatsDir}\{Path.GetFileNameWithoutExtension(cheat)}_aem.pnach", true);
                    Utilities.ParallelLogger.Log($"[INFO] Copied over {Path.GetFileNameWithoutExtension(cheat)}_aem.pnach to {cheatsDir}");
                }
            }
        }
        public static void LoadTextures(List<string> mods, string texturesDir)
        {
            foreach (string dir in mods)
            {
                Utilities.ParallelLogger.Log($"[INFO] Searching for textures in {dir}...");
                if (!Directory.Exists($@"{dir}\texture_override".ToLower()))
                {
                    Utilities.ParallelLogger.Log($"[INFO] No textures folder found in {dir}");
                    continue;
                }

                // Copy over textures
                foreach (var texture in Directory.GetFiles($@"{dir}\texture_override", "*", SearchOption.AllDirectories))
                {
                    List<string> folders = new List<string>(texture.Split(char.Parse("\\")));
                    int idx = folders.IndexOf(Path.GetFileName(dir + "\\texture_override"));
                    folders = folders.Skip(idx + 1).ToList();
                    string binPath = $@"{texturesDir}\{string.Join("\\", folders.ToArray())}";
                    Directory.CreateDirectory(Path.GetDirectoryName(binPath));
                    FileIOWrapper.Copy(texture, binPath, true);
                    Utilities.ParallelLogger.Log($"[INFO] Copied over {Path.GetFileName(texture)} to {binPath}");
                }
            }
        }

        public static void LoadFMVs(List<string> mods, string modDir)
        {
            List<string> copiedFmvs = new List<string>();
            foreach (string dir in mods)
            {
                if(!Directory.Exists($@"{dir}\FMV"))
                    continue;
                if (!Directory.Exists(Path.Combine(modDir, "FMV")))
                    Directory.CreateDirectory(Path.Combine(modDir, "FMV"));

                // Copy over FMVS
                foreach (var fmv in Directory.GetFiles($@"{dir}\FMV", "*.pmsf"))
                {
                    copiedFmvs.Add(Path.GetFileName(fmv));
                    var destinationFmv = Path.Combine(modDir, "FMV", Path.GetFileName(fmv));
                    if (File.Exists(destinationFmv))
                    {
                        if (Utils.SameFiles(fmv, destinationFmv))
                        {
                            Utilities.ParallelLogger.Log($"[INFO] Skipping {fmv} as it is already at {destinationFmv}");
                            continue;
                        }
                    }
                    try
                    {
                        File.Copy(fmv, destinationFmv, true);
                        Utilities.ParallelLogger.Log($"[INFO] Copying {fmv} over {destinationFmv}");
                    } catch(Exception e)
                    {
                        Utilities.ParallelLogger.Log($"[ERROR] Unable to copy {fmv} to {destinationFmv}: {e.Message}");
                    }
                }
            } 
            if (Directory.Exists(Path.Combine(modDir, "FMV")))
            // Delete any FMVs in the P3P FMV folder that weren't from one of the mods
            foreach(var file in Directory.EnumerateFiles(Path.Combine(modDir, "FMV")).Where(f => !copiedFmvs.Contains(Path.GetFileName(f)))) {
                try
                {
                    File.Delete(file);
                    Utilities.ParallelLogger.Log($"[INFO] Deleting unwanted FMV {file}");
                } catch(Exception e)
                {
                    Utilities.ParallelLogger.Log($"[ERROR] Unable to delete unwatned FMV {file}: {e.Message}");
                }
            }
        }

        public static void LoadP3PCheats(List<string> mods, string cheatFile)
        {
            foreach (string dir in mods)
            {
                Utilities.ParallelLogger.Log($"[INFO] Searching for cheats in {dir}...");
                if (!Directory.Exists($@"{dir}\cheats"))
                {
                    Utilities.ParallelLogger.Log($"[INFO] No cheats folder found in {dir}");
                    continue;
                }

                // Work out what cheats should be in the ini
                var existingCheats = PPSSPPCheatFile.ParseCheats(cheatFile);
                foreach (var newCheatFile in Directory.GetFiles($@"{dir}\cheats", "*.ini"))
                {
                    Utilities.ParallelLogger.Log($"[INFO] Applying cheats from {newCheatFile}");
                    var newCheats = PPSSPPCheatFile.ParseCheats(newCheatFile);
                    foreach(var cheat in newCheats.Cheats)
                    {
                        var existingCheat = existingCheats.Cheats.FirstOrDefault(c => c.Name == cheat.Name);
                        if (existingCheat != null)
                            existingCheat.Contents = cheat.Contents;
                        else
                            existingCheats.Cheats.Add(cheat);
                    }
                }

                // Write the ini with cheats in it
                using (StreamWriter writer = new StreamWriter(cheatFile))
                {
                    writer.WriteLine($"_S {existingCheats.GameID}");
                    writer.WriteLine($"_G {existingCheats.GameName}");
                    writer.WriteLine();
                    foreach(var cheat in existingCheats.Cheats)
                    {
                        writer.WriteLine($"_C{(cheat.Enabled ? '1' : '0')} {cheat.Name}");
                        foreach (var line in cheat.Contents)
                            writer.WriteLine(line);
                    }
                }
            }

        }

        internal static void LoadP1PSPCheats(List<string> mods, string cheatFile)
        {
            foreach (string dir in mods)
            {
                Utilities.ParallelLogger.Log($"[INFO] Searching for cheats in {dir}...");
                if (!Directory.Exists($@"{dir}"))
                {
                    Utilities.ParallelLogger.Log($"[INFO] No cheats folder found in {dir}");
                    continue;
                }

                // Work out what cheats should be in the ini
                var existingCheats = PPSSPPCheatFile.ParseCheats(cheatFile);
                foreach (var newCheatFile in Directory.GetFiles($@"{dir}\cheats", "*.ini"))
                {
                    Utilities.ParallelLogger.Log($"[INFO] Applying cheats from {newCheatFile}");
                    var newCheats = PPSSPPCheatFile.ParseCheats(newCheatFile);
                    foreach (var cheat in newCheats.Cheats)
                    {
                        var existingCheat = existingCheats.Cheats.FirstOrDefault(c => c.Name == cheat.Name);
                        if (existingCheat != null)
                            existingCheat.Contents = cheat.Contents;
                        else
                            existingCheats.Cheats.Add(cheat);
                    }
                }

                // Write the ini with cheats in it
                using (StreamWriter writer = new StreamWriter(cheatFile))
                {
                    writer.WriteLine($"_S {existingCheats.GameID}");
                    writer.WriteLine($"_G {existingCheats.GameName}");
                    writer.WriteLine();
                    foreach (var cheat in existingCheats.Cheats)
                    {
                        writer.WriteLine($"_C{(cheat.Enabled ? '1' : '0')} {cheat.Name}");
                        foreach (var line in cheat.Contents)
                            writer.WriteLine(line);
                    }
                }
            }
        }
    }
}
