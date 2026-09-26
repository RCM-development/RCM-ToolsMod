using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using RCM_GUI;
using Unity.Mathematics;
using UnityEngine;
namespace RCM_Tools{

    [BepInDependency(RCMManager.IDENTIFIER, BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin(IDENTIFIER, "Tools Manager", "1.0.0.0")]
    internal class ToolsManager : BaseUnityPlugin{
        const string IDENTIFIER = "RCM.plugins.toolsmanager";
        RCMModUI mod;
        private void Awake(){
            new Harmony(IDENTIFIER).PatchAll();

            RCMManager.ConnectMod("Tools").ContinueWith(t =>{
                mod = t.Result;
                GenerateModUI();
                
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        class RCMTool{
            public string name;
            public string github_link;
            public string tool_path_register_file;
            public bool is_downloading;
        }
        List<RCMTool> tools = new List<RCMTool>(){
            new RCMTool() { name = "RCSE", github_link = "https://api.github.com/repos/RCM-development/RCSE/releases/latest",  tool_path_register_file = Path.Combine(AppContext.BaseDirectory, "RCM", "ToolPaths", "rcse.txt") , is_downloading = false },
            new RCMTool() { name = "RCTEE", github_link = "https://api.github.com/repos/RCM-development/RCTEE/releases/latest",  tool_path_register_file = Path.Combine(AppContext.BaseDirectory, "RCM", "ToolPaths", "rctee.txt"), is_downloading = false }
        };
        void GenerateModUI(){
            mod.ClearFields();
            foreach (var tool in tools){
                if (tool.is_downloading)
                    mod.CreateLabelField($"Downloading {tool.name}...");
                else if (string.IsNullOrEmpty(GetToolPath(tool.tool_path_register_file)))
                    mod.CreateButtonField($"Install {tool.name}", () => InstallTool(tool));
                else mod.CreateButtonField($"Launch {tool.name}", () => LaunchTool(tool));
            }
            mod.CreateButtonField($"Browse Tools Folder", BrowseTools);
        }


        string GetToolPath(string tool_path_register){
            if (File.Exists(tool_path_register)){
                string path = File.ReadAllText(tool_path_register).Trim();
                if (!File.Exists(path) || !path.EndsWith(".exe")){
                    // delete txt file if its got the wrong path
                    RCMManager.Log($"Tool path register file {tool_path_register} is invalid. Deleting it.");
                    File.Delete(tool_path_register);
                    return "";
                } else return path;
            } else return "";
        }

        async void InstallTool(RCMTool tool){
            if (tool.is_downloading) return;

            tool.is_downloading = true;
            GenerateModUI();
            RCMManager.Log($"Downloading latest release of {tool.name}...");

            string installed_path = await DownloadToolLatestRelease(tool.github_link, Path.Combine(AppContext.BaseDirectory, "RCM", tool.name), tool.name);
            if (string.IsNullOrEmpty(installed_path))
                RCMManager.Log($"Failed to download {tool.name}.");
            else if (!File.Exists(installed_path))
                RCMManager.Log($"Downloaded file for {tool.name} is invalid.");
            else{
                // if all good then add the path to the toolpath register
                string dir = Path.GetDirectoryName(tool.tool_path_register_file);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(tool.tool_path_register_file, installed_path);
                RCMManager.Log($"Successfully downloaded {tool.name}.");
            }
            tool.is_downloading = false;
            GenerateModUI();
        }

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;
        void LaunchTool(RCMTool tool){
            // validate tool is installed for real
            string toolPath = GetToolPath(tool.tool_path_register_file);
            if (string.IsNullOrEmpty(toolPath)){
                RCMManager.Log($"Tool path for {tool.name} is not found.");
                return;
            }

            // first we check to see if the process is already running
            string FilePath = Path.GetDirectoryName(toolPath);
            Process[] pList = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(toolPath).ToLower());
            foreach (Process p in pList){
                if (p.MainModule.FileName.StartsWith(FilePath, StringComparison.InvariantCultureIgnoreCase)){
                    // and then we focus this process window
                    IntPtr hWnd = p.MainWindowHandle;
                    if (hWnd != IntPtr.Zero){
                        RCMManager.Log($"focusing: {tool.name}.");
                        ShowWindow(hWnd, SW_RESTORE);
                        SetForegroundWindow(hWnd);
                    }
                    else RCMManager.Log($"cant focus: {tool.name}. probably a command line tool");
                    return;
                }
            }
            try{
                Process.Start(toolPath);
                RCMManager.Log($"launching: {tool.name}.");
            }catch (Exception ex){
                RCMManager.Log($"Failed to launch {tool.name}: {ex.Message}");
            }
        }

        void BrowseTools(){
            string toolsFolder = Path.Combine(AppContext.BaseDirectory, "RCM");
            if (!Directory.Exists(toolsFolder))
                Directory.CreateDirectory(toolsFolder);
            Process.Start("explorer.exe", toolsFolder);
        }

    private static readonly HttpClient http = new HttpClient();
    public static async Task<string> DownloadToolLatestRelease(string github_download_link, string outputFolder, string toolname){
        try
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RCM-ReleaseDownloader");

            // Fetch JSON
            string json = await http.GetStringAsync(github_download_link);

            // Parse JSON using Newtonsoft.Json
            JObject root = JObject.Parse(json);

            JArray assets = (JArray)root["assets"];
            if (assets == null || assets.Count == 0)
                throw new Exception("No downloadable assets found in the latest release!");

            JObject asset = (JObject)assets[0];

            string fileName = asset["name"]?.ToString();
            string downloadUrl = asset["browser_download_url"]?.ToString();

            if (fileName == null || downloadUrl == null)
                throw new Exception("Invalid asset data in release JSON!");

            // Download file bytes
            byte[] data = await http.GetByteArrayAsync(downloadUrl);

            Directory.CreateDirectory(outputFolder);
            string outputPath = Path.Combine(outputFolder, fileName);

            File.WriteAllBytes(outputPath, data);

            // Handle ZIP extraction
            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string extractedFolder = Path.Combine(outputFolder, toolname);
                Directory.CreateDirectory(extractedFolder);

                System.IO.Compression.ZipFile.ExtractToDirectory(outputPath, extractedFolder);
                File.Delete(outputPath);

                // Find .exe inside extracted folder
                foreach (var file in Directory.EnumerateFiles(extractedFolder, "*.exe", SearchOption.AllDirectories))
                    return file;

                return "";
            }
            else if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return outputPath;
            }
        }
        catch (Exception ex)
        {
            RCMManager.Log($"Error downloading latest release of tool {toolname}: {ex.Message}");
        }

        return "";
    }

}



    

}




