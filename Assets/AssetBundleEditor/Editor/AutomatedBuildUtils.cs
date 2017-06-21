using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.Callbacks;

public class AutomatedBuildUtils : Editor
{
    [InitializeOnLoad]
    public class AutoBundleBuilder
    {
        static AutoBundleBuilder()
        {
            EditorApplication.playmodeStateChanged += OnPlaymodeStateChanged;
        }

        private static void OnPlaymodeStateChanged()
        {
            if (!EditorApplication.isPlaying && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (!AssetBundleManager.SimulateAssetBundleInEditor)
                {
                    BuildAssetBundlesWithOptions(BuildAssetBundleOptions.IgnoreTypeTreeChanges | BuildAssetBundleOptions.ChunkBasedCompression);
                }
            }
        }
    }

    private const string AssetBundlesOutputPath = "AssetBundles";

    static void BuildAction(System.Action a_buildAction)
    {
        try
        {
            a_buildAction();
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    public static List<string> GetBuildScenes()
    {
        return (from e in EditorBuildSettings.scenes where e != null where e.enabled select e.path).ToList();
    }

    const string SimulateAssetBundlesMenu = "Custom/Build/Simulate AssetBundles";

    [MenuItem(SimulateAssetBundlesMenu)]
    public static void ToggleSimulateAssetBundle()
    {
        AssetBundleManager.SimulateAssetBundleInEditor = !AssetBundleManager.SimulateAssetBundleInEditor;
    }

    [MenuItem(SimulateAssetBundlesMenu, true)]
    public static bool ToggleSimulateAssetBundleValidate()
    {
        UnityEditor.Menu.SetChecked(SimulateAssetBundlesMenu, AssetBundleManager.SimulateAssetBundleInEditor);
        return true;
    }        

    private static void CreateSymLink(string a_linkSource, string a_linkDestination )
    {
        DeleteSymLink(a_linkDestination);

#if UNITY_EDITOR_WIN
        var proc =  System.Diagnostics.Process.Start("cmd.exe", "/c mklink /J \"" +
                            a_linkDestination.Replace(@"/",@"\") + "\" \"" +
                            a_linkSource.Replace(@"/", @"\") + "\"");
#else
        var proc = System.Diagnostics.Process.Start("ln", " -fs " +
                            a_linkSource + " " + 
                            a_linkDestination);
#endif
        proc.Start();
        proc.WaitForExit();
    }

    private static void DeleteSymLink(string a_linkDestination)
    {
#if UNITY_EDITOR_WIN
        var proc = System.Diagnostics.Process.Start("cmd.exe", "/c rmdir \"" + a_linkDestination.Replace(@"/", @"\") + "\"");
#else
        var proc = System.Diagnostics.Process.Start("rmdir", a_linkDestination);
#endif
        proc.Start();
        proc.WaitForExit();
    }

    static void CopyAssetBundlesTo(string a_outputPath)
    {
        var outputFolder = AssetBundleManager.GetPlatformFolderForAssetBundles(EditorUserBuildSettings.activeBuildTarget);

        // Setup the source folder for assetbundles.
        var source = Path.Combine(Path.Combine(Environment.CurrentDirectory, AssetBundlesOutputPath), outputFolder);
        if (!Directory.Exists(source))
        {
            Debug.Log("No assetBundle output folder, try to build the assetBundles first.");
        }

        // Setup the destination folder for assetbundles.
        //ClearAssetBundles(a_outputPath);
        CreateSymLink(source, a_outputPath);
    }

    private static void ClearAssetBundles(string a_outputPath)
    {
        var destination = a_outputPath;
        if (Directory.Exists(destination))
        {
            FileUtil.DeleteFileOrDirectory(destination);
        }
    }

   
    [MenuItem("Custom/Build/Build AssetBundles")]
    public static void BuildAssetBundles()
    {
        BuildAssetBundlesWithOptions(BuildAssetBundleOptions.ChunkBasedCompression);
    }

    [MenuItem("Custom/Build/Link AssetBundles")]
    private static void LinkAssetBundles()
    {
        var assetBundleOutputPath = Path.Combine(Application.streamingAssetsPath, AssetBundlesOutputPath);

        CopyAssetBundlesTo(assetBundleOutputPath);
    }


    private static void BuildAssetBundlesWithOptions(BuildAssetBundleOptions a_options = BuildAssetBundleOptions.None)
    {
        // Choose the output path according to the build target.
        var outputPath = Path.Combine(AssetBundlesOutputPath,
            AssetBundleManager.GetPlatformFolderForAssetBundles(EditorUserBuildSettings.activeBuildTarget));

        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        BuildPipelineExtended.BuildAssetBundles(outputPath, a_options,
            EditorUserBuildSettings.activeBuildTarget);
    }

    [MenuItem("Custom/Build/Win/32")]
    private static void BuildWin32()
    {
        Build(BuildTarget.StandaloneWindows, BuildTargetGroup.Standalone, null, BuildOptions.None, "_Win32/");
    }
        
    [MenuItem("Custom/Build/Win/64")]
    private static void BuildWin64()
    {
        Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, null, BuildOptions.None, "_Win64/");
    }

    [MenuItem("Custom/Build/OSX")]
    private static void BuildOSX()
    {
        Build(BuildTarget.StandaloneOSXUniversal, BuildTargetGroup.Standalone, null, BuildOptions.None, "_OSX/");
    }
        
        
    private static string GetProductAndBuildExtension(BuildTarget a_buildTarget)
    {
        switch (a_buildTarget)
        {
            case BuildTarget.StandaloneOSXUniversal:
            case BuildTarget.StandaloneOSXIntel:
            case BuildTarget.StandaloneOSXIntel64:
                return PlayerSettings.productName + ".app";
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
                 return PlayerSettings.productName +".exe";
            case BuildTarget.Android:
                return ".apk";
            case BuildTarget.StandaloneLinux:
            case BuildTarget.StandaloneLinux64:
            case BuildTarget.StandaloneLinuxUniversal:
                return PlayerSettings.productName;
            case BuildTarget.PS4:
            case BuildTarget.XboxOne:
                return "";
            default:
                return "";
        }
    }

    private static bool s_copyAssetBundles = true;

    static bool CanPostLinkAssetBundles(BuildTarget a_buildTarget)
    {
        switch (a_buildTarget)
        {
            //case BuildTarget.StandaloneOSXUniversal:
            //case BuildTarget.StandaloneOSXIntel:
            //case BuildTarget.StandaloneWindows:
            //case BuildTarget.StandaloneLinux:
            //case BuildTarget.StandaloneWindows64:
            //case BuildTarget.StandaloneLinux64:
            //case BuildTarget.StandaloneLinuxUniversal:
            //    return true;
            default:
                return false;
        }
    }

    private static void Build(BuildTarget a_buildTarget, BuildTargetGroup a_buildTargetGroup, string a_storeDefine, BuildOptions a_options, string a_buildPostfix, bool a_copyAssetBundles = true, bool a_addRevision = true)
    {
        string buildResult = string.Empty;

        string a_buildPath = "builds/" + PlayerSettings.productName.Replace(" ", "_");
            
        a_buildPath = a_buildPath + a_buildPostfix;
   
        a_buildPath = Path.GetFullPath(a_buildPath) + GetProductAndBuildExtension(a_buildTarget);

        try
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(a_buildTargetGroup,a_buildTarget);

            var assetBundleOutputPath = Path.Combine(Application.streamingAssetsPath, AssetBundlesOutputPath);

            s_copyAssetBundles = a_copyAssetBundles;

            if (!a_copyAssetBundles)
            {
                ClearAssetBundles(assetBundleOutputPath);
            }

            if (CanPostLinkAssetBundles(a_buildTarget))
            {
                ClearAssetBundles(assetBundleOutputPath);
            }
            else
            {
                BuildAssetBundlesWithOptions(BuildAssetBundleOptions.IgnoreTypeTreeChanges |
                                             BuildAssetBundleOptions.ChunkBasedCompression);

                if (a_copyAssetBundles)
                {
                    CopyAssetBundlesTo(assetBundleOutputPath);
                }
            }
                
            var buildDirectory = Path.GetDirectoryName(a_buildPath);

            Directory.CreateDirectory(buildDirectory);

            BuildPipeline.BuildPlayer(GetBuildScenes().ToArray(), a_buildPath, a_buildTarget, a_options);
        }
        catch (Exception ex)
        {
            buildResult = ex.Message;
        }

        if (!string.IsNullOrEmpty(buildResult))
        {
            throw new System.Exception(string.Format("Unable to build ({0}): {1}", PlayerSettings.productName, buildResult));
        }
    }


    public static void BuildCommandLineWin32()
    {
        BuildAction(BuildWin32);
    }
        
    public static void BuildCommandLineWin64()
    {
        BuildAction(BuildWin64);
    }
        
    public static void BuildCommandLineOSX()
    {
        BuildAction(BuildOSX);
    }

    [PostProcessBuild]
    public static void OnPostprocessBuild(BuildTarget a_target, string a_pathToBuiltProject)
    {
        if (CanPostLinkAssetBundles(a_target))
        {
            BuildAssetBundlesWithOptions(BuildAssetBundleOptions.IgnoreTypeTreeChanges | BuildAssetBundleOptions.ChunkBasedCompression);

            var assetBundleOutputPath = Path.GetDirectoryName(a_pathToBuiltProject) + "/" + Path.GetFileNameWithoutExtension(a_pathToBuiltProject) + "_Data/StreamingAssets/" + AssetBundlesOutputPath; 

            if (s_copyAssetBundles)
            {
                CopyAssetBundlesTo(assetBundleOutputPath);
            }
        }
    }
}
