using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;


public class BuildPipelineExtended : MonoBehaviour 
{
    private enum BuildReportSection
    {
        None,
        BundleHeader,
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
        //"$WORKSPACE/$SHARED_SUB_DIRECTORY/unity3d_editor.log"

        var workspace = System.Environment.GetEnvironmentVariable("WORKSPACE");

        var editorLogFilePath = !string.IsNullOrEmpty(workspace) ? workspace : string.Empty;

        var sharedSubDirectory = System.Environment.GetEnvironmentVariable("SHARED_SUB_DIRECTORY");

        if (!string.IsNullOrEmpty(sharedSubDirectory))
        {
            editorLogFilePath += "/" + sharedSubDirectory;
        }
        
        editorLogFilePath += "/unity3d_editor.log";

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
        var assetsSeen = new HashSet<string>();
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
        
        string bundlePath = null;

        using (var editorLog = new StreamReader(File.Open(editorLogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))) 
        {
            editorLog.BaseStream.Seek(0, SeekOrigin.End);

            manifest = BuildPipeline.BuildAssetBundles(a_outputPath, a_assetBundleOptions, a_targetPlatform);

            var section = BuildReportSection.None;

            var bundleLogTempPath = FileUtil.GetUniqueTempPathInProject();
            var bundleLog = new StreamWriter(bundleLogTempPath);

            var assetsTSVTempPath = FileUtil.GetUniqueTempPathInProject();
            var assetsTSV = new StreamWriter(assetsTSVTempPath);

            // 3.9 kb     0.0% Assets/Standard Assets/Glass Refraction (Pro Only)/Sources/Shaders/Glass-Stained-BumpDistort.shader
            var assetSizeAndPath = new Regex(@"^ (\d+\.?\d*) (kb|mb)\t \d+\.?\d*\% ([^\0]+)");
           
            while (true)
            {
                var logLine = editorLog.ReadLine();

                if (logLine == null)
                {
                    break;
                }

                switch (section)
                {
                    case BuildReportSection.None:
                        if (logLine == "-------------------------------------------------------------------------------")
                        {
                            section = BuildReportSection.BundleHeader;
                        }
                       
                        break;
                    case BuildReportSection.BundleHeader:

                        bundleLog.WriteLine(logLine);

                        if (logLine.StartsWith("Bundle Name: "))
                        {
                            bundlePath = logLine.Substring("Bundle Name: ".Length);
                        }
                        else if (logLine == @"Uncompressed usage by category:")
                        {
                            section = BuildReportSection.SectionTotals;
                        }

                        break;
                    case BuildReportSection.SectionTotals:
                        bundleLog.WriteLine(logLine);

                        if (logLine == @"Used Assets and files from the Resources folder, sorted by uncompressed size:")
                        {
                            section = BuildReportSection.AssetSizes;
                        }
                        break;
                    case BuildReportSection.AssetSizes:
                        if (logLine != "-------------------------------------------------------------------------------")
                        {
                            bundleLog.WriteLine(logLine);

                            var match = assetSizeAndPath.Match(logLine);

                            if (match.Success && match.Groups.Count == 4)
                            {
                                var assetPath = match.Groups[3].Value;

                                var fractionalSizeString = match.Groups[1].Value;
                                var sizeUnitsString = match.Groups[2].Value;

                                double fractionalSize;

                                if (double.TryParse(fractionalSizeString, out fractionalSize))
                                {
                                    long bytes;
                                    if (string.Compare(sizeUnitsString, "mb") == 0)
                                    {
                                        bytes = (long) (fractionalSize*1024*1024);
                                    }
                                    else //if(string.Compare(sizeUnitsString,"kb") == 0)
                                    {
                                        bytes = (long) (fractionalSize*1024);
                                    }

                                    assetsTSV.WriteLine(bytes + "\t" + assetPath);

                                    //Only count the first to work around a unity bug 
                                    if (assetsSeen.Add(assetPath))
                                    {
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
                        else
                        {
                            bundleLog.Dispose();
                            assetsTSV.Dispose();

                            var s = a_outputPath + "/" + bundlePath;
                            FileReplace(bundleLogTempPath, s + ".log");
                            FileReplace(assetsTSVTempPath, s + ".tsv");

                            bundleLogTempPath = FileUtil.GetUniqueTempPathInProject();
                            bundleLog = new StreamWriter(bundleLogTempPath);
                            assetsTSVTempPath = FileUtil.GetUniqueTempPathInProject();
                            assetsTSV = new StreamWriter(assetsTSVTempPath);

                            section = BuildReportSection.None;
                        }

                        break;
                }
            }

            bundleLog.Dispose();
            assetsTSV.Dispose();
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
