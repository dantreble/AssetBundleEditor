using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public class BuildPipelineExtended : MonoBehaviour 
{
    private enum BuildReportSection
    {
        Preamble,
        Scenes,
        SectionTotals,
        AssetSizes,
    }

    private static string GetEditorLogFilePath()
    {
        string editorLogFilePath = null;
        string[] pieces = null;
        
        bool winEditor = Application.platform == RuntimePlatform.WindowsEditor;
        
        if (winEditor) 
        {
            editorLogFilePath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            pieces = new string[] {"Unity", "Editor", "Editor.log"};
        } 
        else 
        {
            editorLogFilePath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal);
            pieces = new string[] {"Library","Logs","Unity","Editor.log"};
        }
        
        foreach (var e in pieces) 
        {
            editorLogFilePath = Path.Combine(editorLogFilePath, e);
        }
        
        return editorLogFilePath;
    }

    private static void FileReplace(string a_sourceFileName, string a_destFileName)
    {
        if (File.Exists(a_destFileName))
        {
            File.Delete(a_destFileName);
        }
        File.Move(a_sourceFileName, a_destFileName);
    }

    public static AssetBundleManifest BuildAssetBundles(string a_outputPath, BuildAssetBundleOptions a_assetBundleOptions, BuildTarget a_targetPlatform)
    {
        AssetBundleManifest manifest;

        //Specific to our build machine
        //"$WORKSPACE/unity3d_editor.log"
        var workspace = System.Environment.GetEnvironmentVariable("WORKSPACE");
        var editorLogFilePath = workspace + "/unity3d_editor.log";

        if (!File.Exists(editorLogFilePath))
        {
            editorLogFilePath = GetEditorLogFilePath();

            if (!File.Exists(editorLogFilePath))
            {
                Debug.LogWarning("Editor log file could not be found at: " + editorLogFilePath);
                return BuildPipeline.BuildAssetBundles(a_outputPath, a_assetBundleOptions, a_targetPlatform);
            }
        }

        var allAssetSizes = new Dictionary<string, long>();
        var allAssetSizesPath = a_outputPath+"/assetSizes.tsv";

        var deltaAssetSizes = new Dictionary<string,long>();

        if (File.Exists(allAssetSizesPath))
        {
            using (var allAssetSizesFile = new StreamReader(allAssetSizesPath))
            {
                var assetLine = allAssetSizesFile.ReadLine();
                while (assetLine != null)
                {
                    var tokens = assetLine.Split('\t');

                    long bytes;
                    if (long.TryParse(tokens[0], out bytes))
                    {
                        allAssetSizes.Add(tokens[1], bytes);
                    }

                    assetLine = allAssetSizesFile.ReadLine();
                }
            }
        }
        
       

        int bundleIndex = 0;

        using (var editorLog = new StreamReader(File.Open(editorLogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))) 
        {
            editorLog.BaseStream.Seek(0, SeekOrigin.End);

            var manifestBefore = new Dictionary<string,System.DateTime>();
            
            foreach (var manifestFile in Directory.GetFiles(a_outputPath, "*.manifest"))
            {
                var fileInfo = new FileInfo(manifestFile);

                manifestBefore.Add(manifestFile, fileInfo.LastWriteTime);
            }
            
            manifest = BuildPipeline.BuildAssetBundles(a_outputPath, a_assetBundleOptions, a_targetPlatform);

            var manifestsModified = new Dictionary<string, System.DateTime>();

            foreach (var manifestFile in Directory.GetFiles(a_outputPath, "*.manifest"))
            {
                var fileInfo = new FileInfo(manifestFile);

                if (!manifestBefore.ContainsKey(manifestFile) || manifestBefore[manifestFile] != fileInfo.LastWriteTime)
                {
                    manifestsModified.Add(manifestFile, fileInfo.LastWriteTime);
                }
            }

            var manifestsModifiedList = manifestsModified.OrderBy(a_item => a_item.Value);

            var section = BuildReportSection.Preamble;

            var bundleLogTempPath = FileUtil.GetUniqueTempPathInProject();
            var bundleLog = new StreamWriter(bundleLogTempPath);

            var assetsTSVTempPath = FileUtil.GetUniqueTempPathInProject();
            var assetsTSV = new StreamWriter(assetsTSVTempPath);

            var lastLog = new StreamWriter(a_outputPath + "/last.log");

            // 3.9 kb     0.0% Assets/Standard Assets/Glass Refraction (Pro Only)/Sources/Shaders/Glass-Stained-BumpDistort.shader
            var assetSizeAndPath = new Regex(@"^ (\d+\.?\d*) (kb|mb)\t \d+\.?\d*\% ([^\0]+)");
            
            while (true) 
            {
                var logLine = editorLog.ReadLine();
                
                if (logLine == null) 
                {
                    break;
                }

                lastLog.WriteLine(logLine);

                if(section == BuildReportSection.Preamble && logLine.StartsWith("Level "))
                {
                    section = BuildReportSection.Scenes;
                }
                else if((section == BuildReportSection.Preamble || section == BuildReportSection.Scenes) && logLine.StartsWith("Textures      "))
                {
                    section = BuildReportSection.SectionTotals;
                }
                else if(section == BuildReportSection.SectionTotals && logLine.StartsWith("Used Assets and files"))
                {
                    section = BuildReportSection.AssetSizes;
                    //Skip a line
                    logLine = editorLog.ReadLine();
                }
                else if(section == BuildReportSection.AssetSizes && string.IsNullOrEmpty(logLine))
                {
                    section = BuildReportSection.Preamble;

                    bundleLog.Dispose();
                    assetsTSV.Dispose();

                    //Standalone bundle gets modified without changing manifest
                    if (bundleIndex < manifestsModifiedList.Count())
                    {
                        var manifestPath = manifestsModifiedList.ElementAt(bundleIndex).Key;

                        FileReplace(bundleLogTempPath, Path.ChangeExtension(manifestPath, ".log"));
                        FileReplace(assetsTSVTempPath, Path.ChangeExtension(manifestPath, ".tsv"));

                        //open new temp files
                        bundleIndex++;

                        bundleLogTempPath = FileUtil.GetUniqueTempPathInProject();
                        bundleLog = new StreamWriter(bundleLogTempPath);
                        assetsTSVTempPath = FileUtil.GetUniqueTempPathInProject();
                        assetsTSV = new StreamWriter(assetsTSVTempPath);
                    }
                }

                if (section == BuildReportSection.Scenes || section == BuildReportSection.SectionTotals || section == BuildReportSection.AssetSizes)
                {
                    bundleLog.WriteLine(logLine);
                }

                if(section == BuildReportSection.Scenes)
                {
               
                }

                if(section == BuildReportSection.AssetSizes)
                {
                    var match = assetSizeAndPath.Match(logLine);
                    
                    if(match.Success && match.Groups.Count == 4)
                    {
                        var assetPath = match.Groups[3].Value;

              
                        var fractionalSizeString = match.Groups[1].Value;
                        var sizeUnitsString = match.Groups[2].Value;

                        double fractionalSize;

                        if(double.TryParse(fractionalSizeString, out fractionalSize))
                        {
                            long bytes;
                            if(string.Compare(sizeUnitsString,"mb") == 0)
                            {
                                bytes = (long)(fractionalSize*1024*1024);
                            }
                            else //if(string.Compare(sizeUnitsString,"kb") == 0)
                            {   
                                bytes = (long)(fractionalSize*1024);
                            }
                            
                            assetsTSV.WriteLine(bytes + "\t" + assetPath);

                            long previousSize;
                            if (allAssetSizes.TryGetValue(assetPath, out previousSize))
                            {
                                if (previousSize == bytes)
                                {
                                    continue;
                                }
                                allAssetSizes[assetPath] = bytes;
                                deltaAssetSizes[assetPath] = bytes - previousSize;
                            }
                            else
                            {
                                allAssetSizes[assetPath] = bytes;
                            }
                        }
                    }
                }
            }

            bundleLog.Dispose();
            assetsTSV.Dispose();

            lastLog.Dispose();
        }


        using (var allAssetSizesFile = new StreamWriter(allAssetSizesPath))
        {
            foreach (var allAssetSize in allAssetSizes.OrderByDescending(a_v => a_v.Value).ToList() )
            {
                allAssetSizesFile.WriteLine(allAssetSize.Value + "\t" + allAssetSize.Key);
            }
        }


        var deltaAssetSizesPath = a_outputPath + "/assetSizesDelta.tsv";
        using (var allAssetDeltaSizesFile = new StreamWriter(deltaAssetSizesPath))
        {
            foreach (var allAssetSize in deltaAssetSizes.OrderByDescending(a_v => a_v.Value).ToList())
            {
                allAssetDeltaSizesFile.WriteLine(allAssetSize.Value + "\t" + allAssetSize.Key);
            }
        }


        return manifest;
    }

}
