using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;


public class AssetBundleEditorWindow : EditorWindow
{
    private Dictionary<string, Asset> m_assets;
    private Dictionary<string, Bundle> m_bundles;
    private Dictionary<string, DepGroup> m_depGroups; 

    private List<Asset> m_assetsOrdered;

    private readonly HashSet<string> m_collapsedPaths = new HashSet<string>();

    private SplitterState m_mainVerticalSplit = new SplitterState(new float[] { 50f, 50f }, new int[] { 50, 50 }, null);
    private SplitterState m_upperHorizontalSplit = new SplitterState(new float[] { 30f, 40f, 30f }, new int[] { 30, 40, 30 }, null);
    private SplitterState m_lowerHorizontalSplit = new SplitterState(new float[] { 30f, 70f }, new int[] { 30, 70 }, null);
    private SplitterState m_depVerticalSplit = new SplitterState(new float[] { 40f, 40f, 20f }, new int[] { 40, 40, 20 }, null);

    private HierarchyMode m_mainPaneMode = HierarchyMode.Recurse;

    private Vector2 m_scrollPositionLeftPane;
    private Vector2 m_scrollPositionReverseDepTree;

    private List<Asset> m_selected = new List<Asset>();
    private HashSet<DepGroup> m_selectedGroup = new HashSet<DepGroup>();

    private HierarchyMode m_selectedDepsMode = HierarchyMode.Flat;

    private List<Asset> m_selectedDepsOrdered;
    private bool m_showSingles;
    private Vector2 m_scrollPositionSelectedDeps;
    private long m_totalBundleSize;
    private long m_totalAssetSize;
    private Rect m_bottomBar;
    private Vector2 m_scrollPositionReferencedBundles;
    private string m_SelectedAssetbundleName = "";
    private List<DepGroup> m_depGroupsBySize;
    private Vector2 m_scrollPositionGroupPane;

    private ToolbarSearch m_groupSearch = new ToolbarSearch();
    private ToolbarSearch m_assetListSearch = new ToolbarSearch();
    private ToolbarSearch m_bundleSearch = new ToolbarSearch();
    private ToolbarSearch m_assetSearch = new ToolbarSearch();

    private bool m_stripPath;

    private Bundle m_selectedAssetBundle;
    private HierarchyMode m_assetPaneMode = HierarchyMode.Recurse;
    private HashSet<string> m_assetCollapsed = new HashSet<string>();
    private HashSet<string> m_assetVisited = new HashSet<string>();

    private Vector2 m_scrollPositionBundlePane;
    private Vector2 m_scrollPositionAssetPane;

    private GUIStyle m_columnHeaderStyle;
    private GUIStyle m_foldoutStyle;

    private GUIContent m_assetContent = new GUIContent();

    [MenuItem("Custom/Asset Bundle Editor", false, 101)]
    private static void ShowWindow()
    {
        var window = GetWindow(typeof (AssetBundleEditorWindow));
        window.titleContent = new GUIContent("Asset Bundle Editor");
    }

    private void CalculateAssetsAndBundles()
    {
        var allAssetBundleNames = AssetDatabase.GetAllAssetBundleNames();
        m_bundles = new Dictionary<string, Bundle>(allAssetBundleNames.Length);
        m_assets = new Dictionary<string, Asset>();

        int count = allAssetBundleNames.Length;
        //if (true) count = 2;
        for (var index = 0; index < count; index++)
        {
            EditorUtility.DisplayProgressBar("Loading", "Please Wait", (float)index / count);

            var bundleName = allAssetBundleNames[index];
            var bundle = new Bundle(bundleName);

            var assetPathsFromAssetBundle = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
            bundle.m_directAssets = new List<Asset>(assetPathsFromAssetBundle.Length);

            foreach (var assetPath in assetPathsFromAssetBundle)
            {
                var asset = RecurseAssetDependencies(assetPath, m_assets);
                if (asset != null)
                {
                    asset.m_directBundle = bundle;
                    bundle.m_directAssets.Add(asset);
                }
            }

            m_bundles.Add(bundle.m_name, bundle);
        }
    }

    private static Asset RecurseAssetDependencies(string a_assetPath, Dictionary<string, Asset> a_assets, Asset a_parent = null)
    {
        Asset asset;
        if (a_assets.TryGetValue(a_assetPath, out asset))
        {
            if (a_parent == null)
            {
                return asset;
            }
            if (asset.m_parents == null)
            {
                asset.m_parents = new List<Asset> {a_parent};
            }
            else if (!asset.m_parents.Contains(a_parent))
            {
                asset.m_parents.Add(a_parent);
            }

            return asset;
        }

        if (a_assetPath.EndsWith("cs", StringComparison.CurrentCultureIgnoreCase))
        {
            return null;
        }

        asset = new Asset(a_assetPath);
        a_assets.Add(asset.m_path, asset);

        if (a_parent != null)
        {
            asset.m_parents = new List<Asset> {a_parent};
        }

        if (asset.m_path.EndsWith("fbx", StringComparison.CurrentCultureIgnoreCase))
        {
            return asset; //Don't get deps of fbx, it skews things
        }

        var dependencies = AssetDatabase.GetDependencies(a_assetPath, false);
        if (dependencies.Length <= 0)
        {
            return asset;
        }

        asset.m_dependencies = new List<Asset>(dependencies.Length);
        for (var index = 0; index < dependencies.Length; index++)
        {
            var dependency = RecurseAssetDependencies(dependencies[index], a_assets, asset);
            if (dependency != null)
            {
                asset.m_dependencies.Add(dependency);
            }
        }

        return asset;
    }

    private void LoadAssetSizes()
    {
        const string AssetBundlesOutputPath = "AssetBundles";

        var platformFolderForAssetBundles =
            AssetBundleManager.GetPlatformFolderForAssetBundles(EditorUserBuildSettings.activeBuildTarget);

        var outputPath = Path.Combine(AssetBundlesOutputPath, platformFolderForAssetBundles);
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        var allAssetSizesPath = outputPath + "/assetSizes.tsv";
        if (File.Exists(allAssetSizesPath))
        {
            using (var allAssetSizesFile = new StreamReader(allAssetSizesPath))
            {
                var assetLine = allAssetSizesFile.ReadLine();
                while (assetLine != null)
                {
                    var tokens = assetLine.Split('\t');

                    Asset asset;
                    if (m_assets.TryGetValue(tokens[1], out asset))
                    {
                        long.TryParse(tokens[0], out asset.m_bytes);
                    }

                    assetLine = allAssetSizesFile.ReadLine();
                }
            }
        }
        else
        {  
            //Just guess asset sizes
            foreach (var asset in m_assets)
            {
                var assetPath = asset.Key;

                var bytes = GuessMemoryBytes(assetPath);

                asset.Value.m_bytes = bytes;
            }
        } 
    }

    private static int GuessMemoryBytes(string a_assetPath)
    {
        if (a_assetPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
        {
            return 2662;
        }

        if (a_assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
            || a_assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
            || a_assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
        {
            var length = new FileInfo(a_assetPath).Length;

            return (int) (length/12);
        }

        var importer = AssetImporter.GetAtPath(a_assetPath);

        var textureImporter = importer as TextureImporter;

        if (textureImporter != null)
        {
            return  GuessTextureSizeBytes(textureImporter);
        }

        var shaderImporter = importer as ShaderImporter;

        if (shaderImporter != null)
        {
            return 1024*1024*1; //1mb pure guess
        }


        var audioImporter = importer as AudioImporter;

        if (audioImporter != null)
        {
            var property = typeof (AudioImporter).GetProperty("compSize", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            return (int) property.GetValue(audioImporter, null);
        }
        return 0;
    }

    private static int GetMemoryBitsPerPixel(TextureFormat a_textureFormat)
    {
        switch (a_textureFormat)
        {
            case TextureFormat.Alpha8:
                return 8;
            case TextureFormat.ARGB4444:
                return 16;
            case TextureFormat.RGB24:
                return 32; //Is really 32 in memory
            case TextureFormat.RGBA32:
                return 32;
            case TextureFormat.ARGB32:
                return 32;
            case TextureFormat.RGB565:
                return 16;
            case TextureFormat.R16:
                return 16;
            case TextureFormat.DXT1:
                return 4;
            case TextureFormat.DXT5:
                return 8;
            case TextureFormat.RGBA4444:
                return 16;
            case TextureFormat.BGRA32:
                return 32;
            case TextureFormat.RHalf:
                return 16;
            case TextureFormat.RGHalf:
                return 16*2;
            case TextureFormat.RGBAHalf:
                return 16*4;
            case TextureFormat.RFloat:
                return 32;
            case TextureFormat.RGFloat:
                return 32*2;
            case TextureFormat.RGBAFloat:
                return 32*4;
            case TextureFormat.YUY2:
                return 8 + 8 + (8/4);
            case TextureFormat.BC4:
                return 4;
            case TextureFormat.BC5:
                return 8;
            case TextureFormat.BC6H:
                return 8;
            case TextureFormat.BC7:
                return 8;
            case TextureFormat.DXT1Crunched:
                return 4;
            case TextureFormat.DXT5Crunched:
                return 8;
            default:
                return 8;
        }
    }

    private static int GuessTextureSizeBytes(TextureImporter a_textureImporter)
    {
        var args = new object[] {0, 0};

        var mi = typeof (TextureImporter).GetMethod("GetWidthAndHeight", BindingFlags.NonPublic | BindingFlags.Instance);

        mi.Invoke(a_textureImporter, args);

        TextureFormat textureFormat;
        ColorSpace colorSpace;
        int num;
        a_textureImporter.ReadTextureImportInstructions(EditorUserBuildSettings.activeBuildTarget, out textureFormat, out colorSpace, out num);

        int width = (int) args[0];
        int height = (int) args[1];

        int maxDim = width > height ? width : height;
        int maxTextureSize = a_textureImporter.maxTextureSize;

        if (maxDim > maxTextureSize)
        {
            float scaleFactor = (float) maxTextureSize/(float) maxDim;
            width = (int) (width*scaleFactor);
            height = (int) (height*scaleFactor);
        }

        int bytes = (GetMemoryBitsPerPixel(textureFormat) * width * height) / 8;

        if (a_textureImporter.mipmapEnabled)
        {
            bytes += bytes/3;
        }

        return bytes;
    }

    private void CalculateAssetSize()
    {
        if (m_selectedGroup.Any())
        {
            var assets = new List<Asset>();
            foreach (var group in m_selectedGroup)
            {
                assets.AddRange(group.m_assets);
            }

            m_assetsOrdered = assets.OrderByDescending(a_a => a_a.m_bytes * a_a.References).ToList();
        }
        else
        {
            m_assetsOrdered = m_assets.Values.OrderByDescending(a_a => a_a.m_bytes * a_a.References).ToList();
        }
    }

    private void CalculateReferencedBundles()
    {
        foreach (var asset in m_assets)
        {
            asset.Value.m_bundles = null;
        }

        foreach (var bundleName in AssetDatabase.GetAllAssetBundleNames())
        {
            Bundle bundle;
            if (!m_bundles.TryGetValue(bundleName, out bundle))
            {
                continue;
            }

            foreach (var assetPath in AssetDatabase.GetAssetPathsFromAssetBundle(bundleName))
            {
                Asset asset;
                if (!m_assets.TryGetValue(assetPath, out asset))
                {
                    continue;
                }

                RecurseBundleRefs(asset, bundle);
            }
        }

    }

    private void RecurseBundleRefs(Asset a_asset, Bundle a_parentBundle)
    {
        if (a_asset.m_directBundle != null && a_asset.m_directBundle != a_parentBundle)
        {
            return;
        }

        if (a_asset.m_bundles == null)
        {
            a_asset.m_bundles = new List<Bundle> {a_parentBundle};
        }
        else
        {
            if (a_asset.m_bundles.Contains(a_parentBundle))
            {
                return;
            }

            a_asset.m_bundles.Add(a_parentBundle);
        }

        if (a_asset.m_dependencies == null)
        {
            return;
        }

        foreach (var dependency in a_asset.m_dependencies)
        {
            RecurseBundleRefs(dependency, a_parentBundle);
        }
    }

    private void CalculateAssetSizeRecursive()
    {
        if (m_selectedGroup.Any())
        {
            var assets = new List<Asset>();
            foreach (var group in m_selectedGroup)
            {
                assets.AddRange(group.m_assets);
            }

            m_assetsOrdered = assets.OrderByDescending(a_a => a_a.m_totalBytesWithDeps * a_a.References).ToList();
        }
        else
        {
            m_assetsOrdered = m_assets.Values.OrderByDescending(a_a => a_a.m_totalBytesWithDeps * a_a.References).ToList();

        }
    }

    private void UpdateAssetSizeRecursive()
    {
        foreach (var asset in m_assets)
        {
            asset.Value.m_totalBytesWithDeps = -1;
        }

        foreach (var bundleName in AssetDatabase.GetAllAssetBundleNames())
        {
            Bundle bundle;
            if (!m_bundles.TryGetValue(bundleName, out bundle))
            {
                continue;
            }

            HashSet<string> bundleAssets = null;
            foreach (var assetPath in AssetDatabase.GetAssetPathsFromAssetBundle(bundleName))
            {
                Asset asset;
                if (!m_assets.TryGetValue(assetPath, out asset))
                {
                    continue;
                }

                var visited = new HashSet<string>();
                RecurseAssetSize(asset, asset.m_directBundle, visited);

                if (bundleAssets != null)
                {
                    bundleAssets.UnionWith(visited);
                }
                else
                {
                    bundleAssets = visited;
                }
            }

            bundle.m_totalBytes = bundleAssets != null ? bundleAssets.Sum(a_b => m_assets[a_b].m_bytes) : 0;
        }
    }

    private static long RecurseAssetSize(Asset a_asset, Bundle a_parentBundle, HashSet<string> a_visited)
    {
        if (!a_visited.Add(a_asset.m_path))
        {
            return 0;
        }

        if (a_asset.m_directBundle != null && a_asset.m_directBundle != a_parentBundle)
        {
            return 0;
        }

        if (a_asset.m_totalBytesWithDeps != -1)
        {
            return a_asset.m_totalBytesWithDeps;
        }

        a_asset.m_totalBytesWithDeps = a_asset.m_bytes;

        if (a_asset.m_dependencies == null)
        {
            return a_asset.m_totalBytesWithDeps;
        }

        foreach (var dependency in a_asset.m_dependencies)
        {
            a_asset.m_totalBytesWithDeps += RecurseAssetSize(dependency, a_parentBundle, a_visited);
        }

        return a_asset.m_totalBytesWithDeps;
    }

    private void CalculateBundleSize()
    {
        //foreach (var bundle in m_bundles.Values)
        //{
        //    bundle.m_totalBytes = bundle.m_directAssets.Sum(a_a => a_a.m_totalBytesWithDeps);
        //}
    }

    private void CalculateTotalBundleSize()
    {
        m_totalBundleSize = m_bundles.Values.Sum(a_b => a_b.m_totalBytes);
    }

    private void CalculateTotalAssetSize()
    {
        m_totalAssetSize = m_assets.Values.Sum(a => a.m_bytes);
    }

    private void BuildGroupSizes()
    {
        m_depGroups = new Dictionary<string, DepGroup>();

        foreach (var asset in m_assets)
        {
            var bundleNames = new List<string>(asset.Value.m_bundles.Count);
            bundleNames.AddRange(asset.Value.m_bundles.Select(a_bundle => a_bundle.m_name));
            bundleNames.Sort();

            var bundleString = string.Join(",", bundleNames.ToArray());

            DepGroup depGroup;
            if (m_depGroups.TryGetValue(bundleString, out depGroup))
            {
                depGroup.m_assets.Add(asset.Value);
                depGroup.m_bytes += asset.Value.m_bytes;
            }
            else
            {
                depGroup = new DepGroup(bundleString)
                {
                    m_assets = new List<Asset>() {asset.Value},
                    m_bytes = asset.Value.m_bytes,
                    m_bundles = asset.Value.m_bundles
                };

                m_depGroups.Add(bundleString,depGroup);
            }
        }

        m_depGroupsBySize = m_depGroups.Values.OrderByDescending(a_a => a_a.m_bytes * a_a.m_bundles.Count).ToList();
    } 


    private static string IntToSizeString(long a_inValue)
    {
        if (a_inValue < 0)
        {
            return "unknown";
        }

        var num = (double) a_inValue;

        var array = new[]
        {
            "TB",
            "GB",
            "MB",
            "KB",
            "Bytes"
        };

        var num2 = array.Length - 1;
        while (num > 1000f && num2 >= 0)
        {
            num /= 1000f;
            num2--;
        }
        if (num2 < 0)
        {
            return "<error>";
        }
        return string.Format("{0:#.##} {1}", num, array[num2]);
    }


    private void OnGUI()
    {
        if (m_assets == null)
        {
            if (GUILayout.Button("Load Data"))
            {
                EditorUtility.DisplayProgressBar("Loading", "Please Wait", 0f);

                CalculateAssetsAndBundles();
                LoadAssetSizes();
                CalculateReferencedBundles();
                UpdateAssetSizeRecursive();

                m_mainPaneMode = HierarchyMode.Recurse;
                UpdateAssetsOrdered(m_mainPaneMode);

                CalculateBundleSize();
                CalculateTotalBundleSize();
                CalculateTotalAssetSize();

                BuildGroupSizes();

                EditorUtility.ClearProgressBar();

                m_columnHeaderStyle = new GUIStyle("dockArea");
                m_columnHeaderStyle.alignment = TextAnchor.MiddleLeft;
                m_columnHeaderStyle.padding = new RectOffset(10, 0, 0, 0);
                m_columnHeaderStyle.fontStyle = FontStyle.Bold;

                m_foldoutStyle = new GUIStyle("IN Foldout");
            }
        }
        else
        {
            Toolbar();

            SplitterGUILayout.BeginVerticalSplit(m_mainVerticalSplit);
            {
                SplitterGUILayout.BeginHorizontalSplit(m_upperHorizontalSplit);
                {
                    EditorGUILayout.BeginVertical("box");
                    {
                        GroupPane();
                    }
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.BeginVertical("box");
                    {
                        AssetListPane();
                    }
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.BeginHorizontal();
                    {
                        if (!m_selected.Any())
                        {
                            EditorGUILayout.BeginVertical("box");
                            {
                                GUILayout.BeginHorizontal();
                                {
                                    GUILayout.FlexibleSpace();
                                }
                                GUILayout.EndHorizontal();
                                GUILayout.FlexibleSpace();
                            }
                            EditorGUILayout.EndVertical();
                        }
                        else
                        {
                            SplitterGUILayout.BeginVerticalSplit(m_depVerticalSplit);
                            {
                                EditorGUILayout.BeginVertical("box");
                                {
                                    ReverseDepTree();
                                }
                                EditorGUILayout.EndVertical();

                                EditorGUILayout.BeginVertical("box");
                                {
                                    SelectedDeps();
                                }
                                EditorGUILayout.EndVertical();

                                EditorGUILayout.BeginVertical("box");
                                {
                                    SelectedReferencedBundles();
                                }
                                EditorGUILayout.EndVertical();
                            }
                            SplitterGUILayout.EndVerticalSplit();
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                SplitterGUILayout.EndHorizontalSplit();

                SplitterGUILayout.BeginHorizontalSplit(m_lowerHorizontalSplit);
                {
                    EditorGUILayout.BeginVertical("box");
                    {
                        BundlePane();
                    }
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.BeginVertical("box");
                    {
                        AssetPane();
                    }
                    EditorGUILayout.EndVertical();
                }
                SplitterGUILayout.EndHorizontalSplit();
            }
            SplitterGUILayout.EndVerticalSplit();

            DisplayBottomBar();
        }
    }

    private void Toolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        {
            if (GUILayout.Button("Clear Selection", EditorStyles.toolbarButton))
            {
                m_selected.Clear();
            }

            GUILayout.Space(10);

            m_stripPath = GUILayout.Toggle(m_stripPath, "Strip Path", EditorStyles.toolbarButton);
            m_showSingles = GUILayout.Toggle(m_showSingles, "Show Singles", EditorStyles.toolbarButton);

            GUILayout.FlexibleSpace();
        }
        GUILayout.EndHorizontal();
    }

    private void ReverseDepTree()
    {
        m_scrollPositionReverseDepTree = EditorGUILayout.BeginScrollView(m_scrollPositionReverseDepTree);
        {
            foreach (var selected in m_selected)
            {
                var visitedAssets = new HashSet<string>();
                FoldoutReverseDeps(selected, m_collapsedPaths, visitedAssets, string.Empty, 0f);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void SelectedDeps()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        {
            m_assetListSearch.OnGUI();

            var hierarchyMode = (HierarchyMode)EditorGUILayout.EnumPopup(m_selectedDepsMode, EditorStyles.toolbarPopup);
            if (hierarchyMode != m_mainPaneMode)
            {
                m_selectedDepsMode = hierarchyMode;
                UpdateSelectedDeps();
            }
        }
        GUILayout.EndHorizontal();

        if (m_selectedDepsOrdered == null || !m_selectedDepsOrdered.Any())
        {
            GUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            return;
        }

        int totalWidth = m_upperHorizontalSplit.realSizes[2] - 36; // scrollbar width
        var assetWidth = totalWidth * 0.8f;
        var totalSizeWidth = totalWidth * 0.2f;

        GUILayout.BeginHorizontal();
        {
            GUILayout.Label("Asset", m_columnHeaderStyle, GUILayout.Width(assetWidth));
            GUILayout.Label("Size", m_columnHeaderStyle, GUILayout.Width(totalSizeWidth));
            GUILayout.Label("", m_columnHeaderStyle);
        }
        GUILayout.EndHorizontal();

        m_scrollPositionSelectedDeps = EditorGUILayout.BeginScrollView(m_scrollPositionSelectedDeps);
        {
            foreach (var asset in m_selectedDepsOrdered)
            {
                GUILayout.BeginHorizontal();
                var assetPath = m_stripPath ? asset.m_strippedPath : asset.m_shortPath;
                GUILayout.Label(assetPath, GUILayout.Width(assetWidth));
                GUILayout.Label(asset.GetSizeString(m_selectedDepsMode), GUILayout.Width(totalSizeWidth));
                GUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void SelectedReferencedBundles()
    {
        m_scrollPositionReferencedBundles = EditorGUILayout.BeginScrollView(m_scrollPositionReferencedBundles);
        {
            var bundles = new HashSet<Bundle>();
            foreach (var selection in m_selected)
            {
                bundles.UnionWith(selection.m_bundles);
            }

            foreach (var bundle in bundles)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(bundle.m_name);
                GUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private int m_cachedBundleCount;
    private long m_cachedTotalBundleSize;
    private long m_cachedTotalAssetSize;
    private string m_cachedBottomBarLabelString;

    private string BottomBarLabelString
    {
        get
        {
            int bundleCount = m_bundles != null ? m_bundles.Count : 0;
            if (m_cachedBottomBarLabelString == null || m_cachedBundleCount != bundleCount || m_cachedTotalBundleSize != m_totalBundleSize || m_cachedTotalAssetSize != m_totalAssetSize)
            {
                m_cachedBundleCount = bundleCount;
                m_cachedTotalBundleSize = m_totalBundleSize;
                m_cachedTotalAssetSize = m_totalAssetSize;
                m_cachedBottomBarLabelString = "Asset Size:" + IntToSizeString(m_totalAssetSize) + " | Total size:" + IntToSizeString(m_totalBundleSize) + " | Bundles " + bundleCount;
            }

            return m_cachedBottomBarLabelString;
        }
    }

    private void DisplayBottomBar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        {
            GUILayout.Label(BottomBarLabelString);

            GUILayout.FlexibleSpace();

            GUILayout.TextField(m_SelectedAssetbundleName, "toolbarTextField", GUILayout.MinWidth(200f));

            var existingBundle = m_bundles != null && m_bundles.ContainsKey(m_SelectedAssetbundleName);

            if (GUILayout.Button(existingBundle ? "Set existing" : "Create new", EditorStyles.toolbarButton))
            {
                Bundle newBundle;

                if (m_bundles.TryGetValue(m_SelectedAssetbundleName, out newBundle))
                {
                    newBundle = new Bundle(m_SelectedAssetbundleName);

                    m_bundles.Add(m_SelectedAssetbundleName, newBundle);

                    newBundle.m_directAssets = m_selected;
                }
                else
                {
                    newBundle.m_directAssets.AddRange(m_selected);
                }

                foreach (var selection in m_selected)
                {
                    RemoveSizeRecurseUp(selection, selection.m_totalBytesWithDeps, new HashSet<string>(), selection.m_directBundle);
                }



                //var bundles = m_selected.m_bundles;

                //m_selected.m_bundles = null;
                //m_selected.m_directBundle = newBundle;


                //foreach (var bundle in bundles)
                //{
                //bundle.m_totalBytes = bundle.m_directAssets.Sum(a_a => a_a.m_totalBytesWithDeps);
                //}




            }
        }
        GUILayout.EndHorizontal();
    }

    private void RemoveSizeRecurseUp(Asset a_asset, long a_bytesToRemove, HashSet<string> a_visited, Bundle a_directBundle)
    {
        if (a_asset.m_directBundle != null && a_asset.m_directBundle != a_directBundle)
        {
            return;
        }

        foreach (var parent in a_asset.m_parents)
        {
            if (a_visited.Add(parent.m_path))
            {
                parent.m_totalBytesWithDeps -= a_bytesToRemove;

                RemoveSizeRecurseUp(parent, a_bytesToRemove, a_visited, a_directBundle);
            }
        }
    }

    private void GroupPane()
    {
        if (m_depGroupsBySize == null || !m_depGroupsBySize.Any())
        {
            return;
        }

        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        {
            m_groupSearch.OnGUI();
            GUILayout.FlexibleSpace();
        }
        GUILayout.EndHorizontal();

        int totalWidth = m_upperHorizontalSplit.realSizes[0] - 40; // scrollbar width
        var assetWidth = totalWidth * 0.8f;
        var refWidth = totalWidth * 0.05f;
        var totalSizeWidth = totalWidth * 0.15f;

        GUILayout.BeginHorizontal();
        {
            GUILayout.Label("Bundles", m_columnHeaderStyle, GUILayout.Width(assetWidth));
            GUILayout.Label("#", m_columnHeaderStyle, GUILayout.Width(refWidth));
            GUILayout.Label("Size", m_columnHeaderStyle, GUILayout.Width(totalSizeWidth));
            GUILayout.Label("", m_columnHeaderStyle);
        }
        GUILayout.EndHorizontal();

        m_scrollPositionGroupPane = EditorGUILayout.BeginScrollView(m_scrollPositionGroupPane);

        var count = Mathf.Min(m_depGroupsBySize.Count, 100);

        var displayed = 0;

        for (var index = 0; index < m_depGroupsBySize.Count && displayed < count; index++)
        {
            var group = m_depGroupsBySize[index];

            if (!m_showSingles && group.m_bundles.Count == 1)
            {
                continue;
            }

            var groupName = group.m_bundleDepString;
            if (!m_groupSearch.Check(groupName))
            {
                continue;
            }

            var selected = m_selectedGroup.Contains(group);

            GUILayout.BeginHorizontal();
            {
                var nowSelected = GUILayout.Toggle(selected, groupName, GUILayout.Width(assetWidth));
                GUILayout.Label(group.BundleCountString, GUILayout.Width(refWidth));
                GUILayout.Label(group.SizeString, GUILayout.Width(totalSizeWidth));

                if (selected != nowSelected)
                {
                    if (nowSelected)
                    {
                        m_selectedGroup.Add(group);
                    }
                    else
                    {
                        m_selectedGroup.Remove(group);
                    }

                    UpdateAssetsOrdered(m_mainPaneMode);
                }
            }
            GUILayout.EndHorizontal();

            displayed++;
        }

        EditorGUILayout.EndScrollView();
    }

    private void AssetListPane()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        {
            m_assetListSearch.OnGUI();

            var mainPaneMode = (HierarchyMode)EditorGUILayout.EnumPopup(m_mainPaneMode, EditorStyles.toolbarPopup);
            if (mainPaneMode != m_mainPaneMode)
            {
                UpdateAssetsOrdered(mainPaneMode);
                m_mainPaneMode = mainPaneMode;
            }
        }
        GUILayout.EndHorizontal();

        if (m_assetsOrdered == null)
        {
            return;
        }

        int totalWidth = m_upperHorizontalSplit.realSizes[1] - 40; // scrollbar width
        var assetWidth = totalWidth * 0.8f;
        var refWidth = totalWidth * 0.05f;
        var totalSizeWidth = totalWidth * 0.15f;

        GUILayout.BeginHorizontal();
        {
            GUILayout.Label("Asset", m_columnHeaderStyle, GUILayout.Width(assetWidth));
            GUILayout.Label("#", m_columnHeaderStyle, GUILayout.Width(refWidth));
            GUILayout.Label("Size", m_columnHeaderStyle, GUILayout.Width(totalSizeWidth));
            GUILayout.Label("", m_columnHeaderStyle);
        }
        GUILayout.EndHorizontal();

        m_scrollPositionLeftPane = EditorGUILayout.BeginScrollView(m_scrollPositionLeftPane);
        {
            var count = Mathf.Min(m_assetsOrdered.Count, 100);
            var displayed = 0;
            for (var index = 0; index < m_assetsOrdered.Count && displayed < count; index++)
            {
                var asset = m_assetsOrdered[index];
                if (!m_showSingles && asset.References == 1)
                {
                    continue;
                }

                var assetPath = m_stripPath ? asset.m_strippedPath : asset.m_shortPath;
                if (!m_assetListSearch.Check(assetPath))
                {
                    continue;
                }

                displayed++;

                GUILayout.BeginHorizontal();
                {
                    var selected = m_selected.Contains(asset);
                    var nowSelected = GUILayout.Toggle(selected, assetPath, GUILayout.Width(assetWidth));
                    GUILayout.Label(asset.ReferencesString, GUILayout.Width(refWidth));
                    GUILayout.Label(asset.GetTotalSizeString(m_mainPaneMode), GUILayout.Width(totalSizeWidth));

                    if (selected != nowSelected)
                    {
                        if (nowSelected)
                        {
                            UpdateSelected(asset, true);
                        }
                        else
                        {
                            UpdateSelected(asset, false);
                        }
                    }
                }
                GUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void UpdateAssetsOrdered(HierarchyMode a_mainPaneMode)
    {
        switch (a_mainPaneMode)
        {
            case HierarchyMode.Flat:
                CalculateAssetSize();
                break;
            case HierarchyMode.Recurse:
                CalculateAssetSizeRecursive();
                break;
        }
    }

    private void UpdateSelected(Asset a_asset, bool a_add)
    {
        if (a_add == m_selected.Contains(a_asset))
        {
            return;
        }

        if (a_add)
        {
            m_selected.Add(a_asset);
        }
        else
        {
            m_selected.Remove(a_asset);
        }

        if (!m_selected.Any())
        {
            m_collapsedPaths.Clear();
        }

        UpdateSelectedDeps();

        if (a_asset == null)
        {
            return;
        }


        var asset = AssetDatabase.LoadMainAssetAtPath(a_asset.m_path);
        if (asset != null)
        {
            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }
    }

    private void UpdateSelectedDeps()
    {
        var deps = new HashSet<Asset>();

        foreach (var selected in m_selected)
        {
            CollectDepsRecurse(selected, deps);
        }

        if (m_selectedDepsMode == HierarchyMode.Recurse)
        {
            m_selectedDepsOrdered = deps.OrderByDescending(a_a => a_a.m_totalBytesWithDeps).ToList();
        }
        else
        {
            m_selectedDepsOrdered = deps.OrderByDescending(a_a => a_a.m_bytes).ToList();
        }
    }

    private void CollectDepsRecurse(Asset a_asset, HashSet<Asset> a_deps)
    {
        if (!a_deps.Add(a_asset))
        {
            return;
        }

        if (a_asset.m_dependencies == null)
        {
            return;
        }

        foreach (var dependency in a_asset.m_dependencies)
        {
            CollectDepsRecurse(dependency, a_deps);
        }
    }

    private void FoldoutReverseDeps(Asset a_asset, HashSet<string> a_expanded, HashSet<string> a_visited, string a_path, float a_indent)
    {
        var hasParents = a_asset.m_parents != null;
        var path = a_path + @"/" + a_asset.m_path;
        var nowExpanded = false;

        EditorGUILayout.BeginHorizontal();
        {
            GUILayout.Space(a_indent);

            var foldoutRect = GUILayoutUtility.GetRect(GUIContent.none, m_foldoutStyle);
            if (hasParents)
            {
                var expanded = a_expanded.Contains(path);
                nowExpanded = GUI.Toggle(foldoutRect, expanded, GUIContent.none, m_foldoutStyle);
                if (expanded != nowExpanded)
                {
                    if (nowExpanded)
                    {
                        a_expanded.Add(path);
                    }
                    else
                    {
                        a_expanded.Remove(path);
                    }
                }
            }

            m_assetContent.text = m_stripPath ? a_asset.m_strippedPath : a_asset.m_shortPath;
            var assetRect = GUILayoutUtility.GetRect(m_assetContent, GUI.skin.label);
            GUI.Label(assetRect, m_assetContent);
            if (GUI.Button(assetRect, GUIContent.none, GUIStyle.none))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(a_asset.m_path);
                if (asset != null)
                {
                    EditorGUIUtility.PingObject(asset);
                    Selection.activeObject = asset;
                }
            }

            GUILayout.FlexibleSpace();
        }
        EditorGUILayout.EndHorizontal();

        if (!nowExpanded || !a_visited.Add(a_asset.m_path))
        {
            return;
        }

        foreach (var parentAsset in a_asset.m_parents)
        {
            FoldoutReverseDeps(parentAsset, a_expanded, a_visited, path, a_indent + 20f);
        }
    }

    private void BundlePane()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        {
            m_bundleSearch.OnGUI();
            GUILayout.FlexibleSpace();
        }
        GUILayout.EndHorizontal();

        int totalWidth = m_lowerHorizontalSplit.realSizes[0] - 24; // scrollbar width
        var assetWidth = totalWidth * 0.85f;
        var totalSizeWidth = totalWidth * 0.15f;

        GUILayout.BeginHorizontal();
        {
            GUILayout.Label("Bundle", m_columnHeaderStyle, GUILayout.Width(assetWidth));
            GUILayout.Label("Size", m_columnHeaderStyle, GUILayout.Width(totalSizeWidth));
            GUILayout.Label("", m_columnHeaderStyle);
        }
        GUILayout.EndHorizontal();

        m_scrollPositionBundlePane = EditorGUILayout.BeginScrollView(m_scrollPositionBundlePane);
        {
            foreach (var bundlePair in m_bundles)
            {
                var bundle = bundlePair.Value;
                var bundleName = bundle.m_name;

                if (!m_bundleSearch.Check(bundleName))
                {
                    continue;
                }

                GUILayout.BeginHorizontal();
                {
                    bool selected = bundle == m_selectedAssetBundle;
                    var nowSelected = GUILayout.Toggle(selected, bundleName);
                    GUILayout.Label(bundle.SizeString, GUILayout.Width(totalSizeWidth));

                    if (selected != nowSelected)
                    {
                        Select(bundle, nowSelected);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void AssetPane()
    {
        if (m_selectedAssetBundle == null)
        {
            GUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            return;
        }

        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        {
            m_assetSearch.OnGUI();

            var hierarchyMode = (HierarchyMode)EditorGUILayout.EnumPopup(m_assetPaneMode, EditorStyles.toolbarPopup);
            if (hierarchyMode != m_assetPaneMode)
            {
                m_assetPaneMode = hierarchyMode;
            }

            GUILayout.FlexibleSpace();
        }
        GUILayout.EndHorizontal();

        int totalWidth = m_lowerHorizontalSplit.realSizes[1] - 54; // scrollbar width
        var assetWidth = totalWidth * 0.8f;
        var refWidth = totalWidth * 0.05f;
        var totalSizeWidth = totalWidth * 0.15f;

        GUILayout.BeginHorizontal();
        {
            GUILayout.Label("Asset", m_columnHeaderStyle, GUILayout.Width(assetWidth));
            GUILayout.Label("#", m_columnHeaderStyle, GUILayout.Width(refWidth));
            GUILayout.Label("Size", m_columnHeaderStyle, GUILayout.Width(totalSizeWidth));
            GUILayout.Label("", m_columnHeaderStyle);
        }
        GUILayout.EndHorizontal();

        m_scrollPositionAssetPane = EditorGUILayout.BeginScrollView(m_scrollPositionAssetPane);
        {
            for (var i = 0; i < m_selectedAssetBundle.m_directAssets.Count; ++i)
            {
                var asset = m_selectedAssetBundle.m_directAssets[i];

                var color = GUI.color;
                if (m_selectedAssetBundle.m_name.StartsWith("shared_") && asset.Parents == 0)
                {
                    GUI.color = Color.yellow;
                }

                AssetPaneAsset(asset, "", assetWidth, refWidth, totalSizeWidth, 0, m_assetCollapsed, m_assetVisited);

                GUI.color = color;
            }
            m_assetVisited.Clear();
        }
        EditorGUILayout.EndScrollView();
    }

    private void AssetPaneAsset(Asset a_asset, string a_fullPath, float a_assetWidth, float a_refWidth, float a_totalSizeWidth, int a_indentLevel, HashSet<string> a_collapsed, HashSet<string> a_visited)
    {
        if (a_indentLevel > 0)
        {
            var assetImporter = AssetImporter.GetAtPath(a_asset.m_path);
            if (assetImporter == null || !string.IsNullOrEmpty(assetImporter.assetBundleName))
            {
                return;
            }
        }

        var fullPath = a_fullPath + "/" + a_asset.m_shortPath;
        bool nowExpanded = true;

        var assetName = m_stripPath ? a_asset.m_strippedPath : a_asset.m_shortPath;
        if (m_assetSearch.Check(assetName))
        {
            GUILayout.BeginHorizontal();
            {
                float indent = m_assetPaneMode == HierarchyMode.Recurse ? a_indentLevel * 15f : 0f;
                float assetWidth = Mathf.Max(0f, a_assetWidth - indent);
                GUILayout.Space(indent);

                var foldoutRect = GUILayoutUtility.GetRect(GUIContent.none, m_foldoutStyle);
                if (a_asset.m_dependencies != null)
                {
                    var expanded = !a_collapsed.Contains(fullPath);
                    nowExpanded = GUI.Toggle(foldoutRect, expanded, GUIContent.none, m_foldoutStyle);
                    if (expanded != nowExpanded)
                    {
                        if (nowExpanded)
                        {
                            a_collapsed.Remove(fullPath);
                        }
                        else
                        {
                            a_collapsed.Add(fullPath);
                        }
                    }
                }

                m_assetContent.text = assetName;
                var assetRect = GUILayoutUtility.GetRect(m_assetContent, GUI.skin.label, GUILayout.Width(assetWidth));
                GUI.Label(assetRect, m_assetContent);
                if (GUI.Button(assetRect, GUIContent.none, GUIStyle.none))
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(a_asset.m_path);
                    if (asset != null)
                    {
                        EditorGUIUtility.PingObject(asset);
                        Selection.activeObject = asset;
                    }
                }

                GUILayout.Label(a_asset.ParentsString, GUILayout.Width(a_refWidth));
                GUILayout.Label(a_asset.GetSizeString(m_assetPaneMode), GUILayout.Width(a_totalSizeWidth));
            }
            GUILayout.EndHorizontal();
        }

        if (!nowExpanded || !a_visited.Add(a_asset.m_path))
        {
            return;
        }

        if (a_asset.m_dependencies != null)
        {
            var color = GUI.color;
            GUI.color = new Color(Mathf.Min(0.8f, color.r), Mathf.Min(0.8f, color.g), Mathf.Min(0.8f, color.b));
            for (int i = 0; i < a_asset.m_dependencies.Count; ++i)
            {
                var dependency = a_asset.m_dependencies[i];
                AssetPaneAsset(dependency, fullPath, a_assetWidth, a_refWidth, a_totalSizeWidth, a_indentLevel + 1, a_collapsed, a_visited);
            }
            GUI.color = color;
        }
    }

    private void Select(Bundle a_bundle, bool a_select)
    {
        if (a_select)
        {
            m_selectedAssetBundle = a_bundle;
            m_assetCollapsed.Clear();
        }
        else if (m_selectedAssetBundle == a_bundle)
        {
            m_selectedAssetBundle = null;
            m_assetCollapsed.Clear();
        }
    }

    private enum HierarchyMode
    {
        Flat,
        Recurse
    }

    private class Bundle
    {

        //
        public readonly string m_name;
        public List<Asset> m_directAssets;

        //
        public long m_totalBytes;

        public Bundle(string a_name)
        {
            m_name = a_name;
        }

        public override int GetHashCode()
        {
            return m_name.GetHashCode();
        }

        private long m_cachedSize;
        private string m_cachedSizeString;

        public string SizeString
        {
            get
            {
                if (m_cachedSizeString == null || m_cachedSize != m_totalBytes)
                {
                    m_cachedSize = m_totalBytes;
                    m_cachedSizeString = IntToSizeString(m_totalBytes);
                }

                return m_cachedSizeString;
            }
        }
    }

    private class Asset
    {
        //
        public readonly string m_path;
        public readonly string m_shortPath;
        public readonly string m_strippedPath;

        //
        public List<Bundle> m_bundles;

        //
        public long m_bytes;

        public List<Asset> m_dependencies;

        public Bundle m_directBundle;
        public List<Asset> m_parents;
        
        //
        public long m_totalBytesWithDeps;

        public Asset(string a_path)
        {
            m_path = a_path;
            m_shortPath = a_path.Replace("Assets/", string.Empty);
            m_strippedPath = Path.GetFileName(m_shortPath);
        }

        public int References
        {
            get { return m_bundles != null ? m_bundles.Count : 1; }
        }

        public int Parents
        {
            get { return m_parents != null ? m_parents.Count : 0; }
        }

        public override int GetHashCode()
        {
            return m_path.GetHashCode();
        }

        private int m_cachedParents;
        private string m_cachedParentsString;

        public string ParentsString
        {
            get
            {
                int parents = Parents;
                if (m_cachedParentsString == null || m_cachedParents != parents)
                {
                    m_cachedParents = parents;
                    m_cachedParentsString = parents.ToString();
                }

                return m_cachedParentsString;
            }
        }

        private int m_cachedReferences;
        private string m_cachedReferencesString;

        public string ReferencesString
        {
            get
            {
                int references = References;
                if (m_cachedReferencesString == null || m_cachedReferences != references)
                {
                    m_cachedReferences = references;
                    m_cachedReferencesString = m_cachedReferences.ToString();
                }

                return m_cachedReferencesString;
            }
        }

        private long m_cachedFlatSize;
        private string m_cachedFlatSizeString;

        public string FlatSizeString
        {
            get
            {
                long size = m_bytes;
                if (m_cachedFlatSizeString == null || m_cachedFlatSize != size)
                {
                    m_cachedFlatSize = size;
                    m_cachedFlatSizeString = IntToSizeString(size);
                }

                return m_cachedFlatSizeString;
            }
        }

        private long m_cachedRecurseSize;
        private string m_cachedRecurseSizeString;

        public string RecurseSizeString
        {
            get
            {
                long size = m_totalBytesWithDeps;
                if (m_cachedRecurseSizeString == null || m_cachedRecurseSize != size)
                {
                    m_cachedRecurseSize = size;
                    m_cachedRecurseSizeString = IntToSizeString(size);
                }

                return m_cachedRecurseSizeString;
            }
        }

        public string GetSizeString(HierarchyMode a_mode)
        {
            return a_mode == HierarchyMode.Flat ? FlatSizeString : RecurseSizeString;
        }

        private long m_cachedTotalSize;
        private string m_cachedTotalSizeString;

        public string GetTotalSizeString(HierarchyMode a_mode)
        {
            long size = a_mode == HierarchyMode.Flat ? m_bytes : m_totalBytesWithDeps;
            long totalSize = size * References;
            if (m_cachedTotalSizeString == null || m_cachedTotalSize != totalSize)
            {
                m_cachedTotalSize = totalSize;
                m_cachedTotalSizeString = IntToSizeString(totalSize);
            }

            return m_cachedTotalSizeString;
        }
    }

    private class DepGroup
    {
        //
        public readonly string m_bundleDepString;

        public List<Asset> m_assets;
        public List<Bundle> m_bundles;
        public long m_bytes;

        public DepGroup(string a_bundleDepString)
        {
            m_bundleDepString = a_bundleDepString;
        }

        private long m_cachedSize;
        private string m_cachedSizeString;

        public string SizeString
        {
            get
            {
                long size = m_bytes * m_bundles.Count;
                if (m_cachedSizeString == null || m_cachedSize != size)
                {
                    m_cachedSize = size;
                    m_cachedSizeString = IntToSizeString(m_cachedSize);
                }

                return m_cachedSizeString;
            }
        }

        private int m_cachedBundleCount;
        private string m_cachedBundleCountString;

        public string BundleCountString
        {
            get
            {
                if (m_cachedBundleCountString == null || m_cachedBundleCount != m_bundles.Count)
                {
                    m_cachedBundleCount = m_bundles.Count;
                    m_cachedBundleCountString = m_cachedBundleCount.ToString();
                }

                return m_cachedBundleCountString;
            }
        }
    }
}
