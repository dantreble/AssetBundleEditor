using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;
#endif

/*
 	In this demo, we demonstrate:
	1.	Automatic asset bundle dependency resolving & loading.
		It shows how to use the manifest assetbundle like how to get the dependencies etc.
	2.	Automatic unloading of asset bundles (When an asset bundle or a dependency thereof is no longer needed, the asset bundle is unloaded)
	3.	Editor simulation. A bool defines if we load asset bundles from the project or are actually using asset bundles(doesn't work with assetbundle variants for now.)
		With this, you can player in editor mode without actually building the assetBundles.
	4.	Optional setup where to download all asset bundles
	5.	Build pipeline build postprocessor, integration so that building a player builds the asset bundles and puts them into the player data (Default implmenetation for loading assetbundles from disk on any platform)
	6.	Use WWW.LoadFromCacheOrDownload and feed 128 bit hash to it when downloading via web
		You can get the hash from the manifest assetbundle.
	7.	AssetBundle variants. A prioritized list of variants that should be used if the asset bundle with that variant exists, first variant in the list is the most preferred etc.
*/

// Loaded assetBundle contains the references count which can be used to unload dependent assetBundles automatically.
public class LoadedAssetBundle
{
    public AssetBundle m_AssetBundle;
    public int m_ReferencedCount;

    public LoadedAssetBundle(AssetBundle assetBundle)
    {
        m_AssetBundle = assetBundle;
        m_ReferencedCount = 1;
    }
}

// Class takes care of loading assetBundle and its dependencies automatically, loading variants automatically.
public class AssetBundleManager : MonoBehaviour, IGlobalSingleton
{
    private readonly Dictionary<string, string[]> m_dependencies = new Dictionary<string, string[]>();
    private readonly Dictionary<string, string> m_downloadingErrors = new Dictionary<string, string>();
    private readonly Dictionary<string, AssetBundleLoadOperation> m_inProgressOperationsByName = new Dictionary<string, AssetBundleLoadOperation>();

    private readonly Dictionary<string, LoadedAssetBundle> m_loadedAssetBundles = new Dictionary<string, LoadedAssetBundle>();

    private readonly List<KeyValuePair<string, AssetBundleLoadOperation>> m_inProgressOperations = new List<KeyValuePair<string, AssetBundleLoadOperation>>();
    private readonly List<KeyValuePair<string, AssetBundleLoadOperation>> m_completedOperations = new List<KeyValuePair<string, AssetBundleLoadOperation>>();

    private AssetBundleManifest m_assetBundleManifest;
    private string m_baseDownloadingUrl = "";
    private string[] m_variants = { };

    #region IGlobalSingleton

    public bool HasInstance
    {
        get { return Instance != null; }
    }

    #endregion

	// Initialize the downloading url and AssetBundleManifest object.
    private void Awake()
    {
        Instance = this;
    }

	private IEnumerator Start()
	{
		#if UNITY_EDITOR
		Debug.Log("We are " + (SimulateAssetBundleInEditor ? "in Editor simulation mode" : "in normal mode"));
		#endif
		
#if UNITY_EDITOR
		string platformFolderForAssetBundles = GetPlatformFolderForAssetBundles(EditorUserBuildSettings.activeBuildTarget);
#else
		string platformFolderForAssetBundles =  GetPlatformFolderForAssetBundles(Application.platform); 
#endif

        // Set base downloading url.
        string relativePath = GetRelativePath();
#if UNITY_EDITOR
        BaseDownloadingURL = relativePath + kAssetBundlesPath + platformFolderForAssetBundles + "/";
#else
        BaseDownloadingURL = relativePath + kAssetBundlesPath + "/";
#endif

        // Initialize AssetBundleManifest which loads the AssetBundleManifest object.

        var request = Initialize(platformFolderForAssetBundles);

		if (request != null)
		{
			yield return StartCoroutine(request);
		}
	}

    void OnDestroy()
    {
        //unload all the asset bundles
        foreach (var loadedAssetBundle in m_loadedAssetBundles)
        {
            loadedAssetBundle.Value.m_AssetBundle.Unload(false);
        }

        Instance = null;
    }
    
    const string kAssetBundlesPath = "/AssetBundles/";

	public bool IsInitialized()
    {
#if UNITY_EDITOR
	    if (SimulateAssetBundleInEditor)
	    {
	        return true;
	    }
#endif
		return m_assetBundleManifest != null;
    }

#if UNITY_EDITOR
    public static string GetPlatformFolderForAssetBundles(BuildTarget a_target)
    {
        switch (a_target)
        {
            case BuildTarget.Android:
                return "Android";
            case BuildTarget.iOS:
                return "iOS";
            //case BuildTarget.WebPlayer:
            //    return "WebPlayer";
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
            case BuildTarget.StandaloneOSXIntel:
            case BuildTarget.StandaloneOSXIntel64:
            case BuildTarget.StandaloneOSXUniversal:
			case BuildTarget.StandaloneLinux:
			case BuildTarget.StandaloneLinux64:
			case BuildTarget.StandaloneLinuxUniversal:
				return "Standalone";
			case BuildTarget.PS4:
				return "PS4";
			case BuildTarget.XboxOne:
				return "XboxOne";
            case BuildTarget.PSP2:
                return "PSP";
            // Add more build targets for your own.
            // If you add more targets, don't forget to add the same platforms to GetPlatformFolderForAssetBundles(RuntimePlatform) function.
            default:
                return null;
        }
    }
#endif

    static string GetPlatformFolderForAssetBundles(RuntimePlatform a_platform)
    {
        switch (a_platform)
        {
            case RuntimePlatform.Android:
                return "Android";
            case RuntimePlatform.IPhonePlayer:
                return "iOS";
            //case RuntimePlatform.WindowsWebPlayer:
            //case RuntimePlatform.OSXWebPlayer:
            //    return "WebPlayer";
            case RuntimePlatform.WindowsPlayer:
            case RuntimePlatform.OSXPlayer:
			case RuntimePlatform.LinuxPlayer:
				return "Standalone";
			case RuntimePlatform.PS4:
				return "PS4";
			case RuntimePlatform.XboxOne:
				return "XboxOne";
            case RuntimePlatform.PSP2:
                return "PSP";

            // Add more build platform for your own.
            // If you add more platforms, don't forget to add the same targets to GetPlatformFolderForAssetBundles(BuildTarget) function.
            default:
                return null;
        }
    }

    static public string GetRelativePath()
    {
        if (Application.isEditor)
        {
            return System.Environment.CurrentDirectory.Replace("\\", "/"); // Use the build output folder directly.
        }

        if (Application.isWebPlayer)
        {
            return System.IO.Path.GetDirectoryName(Application.absoluteURL).Replace("\\", "/") + "/StreamingAssets";
        }

        if (Application.isMobilePlatform || Application.isConsolePlatform)
        {
            return Application.streamingAssetsPath;
        }

        // For standalone player.
        return Application.streamingAssetsPath;
    }

	public static void RefToBundleAndAsset(string a_reference, out string a_assetBundleName, out string a_assetName)
	{
		var split = a_reference.Split(':');
		
		a_assetBundleName = split.Length > 0 ? split[0] : null;
		a_assetName = split.Length > 1 ? ("Assets/" + split[1]) : null;
	}

	public static void RefToBundleAssetGUID(string a_reference, out string a_assetBundleName, out string a_assetName, out string a_guid)
	{
        var split = a_reference.Split(':');
			
		a_assetBundleName = split.Length > 0 ? split[0] : null;
		a_assetName = split.Length > 1 ? ("Assets/" + split[1]) : null;
		a_guid = split.Length > 2 ? split[2] : null;
	}

	public static string RefToAsset(string a_reference)
	{
		var split = a_reference.Split(':');

		return split.Length > 1 ? ("Assets/" + split[1]) : null;
	}

	public static string RefToGUID(string a_reference)
	{
		var split = a_reference.Split(':');
		
		return split.Length > 2 ? split[2] : null;
	}

	public static string BundleAndAssetToRef( string a_bundle, string a_path, string a_guid )
	{
	    if (a_bundle == null)
	    {
	        a_bundle = string.Empty;
	    }

        if (a_path == null)
        {
            a_path = string.Empty;
        }

        if (a_guid == null)
        {
            a_guid = string.Empty;
        }

        return a_bundle + ':' + a_path.Replace("Assets/", string.Empty) + ':' + a_guid;
	}

#if UNITY_EDITOR
    public static string GetBundleName(string a_path)
    {
        var assetImporter = AssetImporter.GetAtPath(a_path);
        if (assetImporter != null)
        {
            if (!string.IsNullOrEmpty(assetImporter.assetBundleName))
            {
                return assetImporter.assetBundleName;
            }

            var parentPath = System.IO.Path.GetDirectoryName(a_path);
            return GetBundleName(parentPath);
        }

        return null;
    }

    public static string AssetToRef(UnityEngine.Object a_asset)
    {
        string path = AssetDatabase.GetAssetPath(a_asset);
        var bundleName = GetBundleName(path);
        if (!string.IsNullOrEmpty(bundleName))
        {
            return BundleAndAssetToRef(bundleName, path, AssetDatabase.AssetPathToGUID(path));
        }

        return null;
    }
#endif


    // Get loaded AssetBundle, only return vaild object when all the dependencies are downloaded successfully.
    public LoadedAssetBundle GetLoadedAssetBundle(string a_assetBundleName, out string a_error)
    {
        if (m_downloadingErrors.TryGetValue(a_assetBundleName, out a_error))
        {
            return null;
        }

        LoadedAssetBundle bundle = null;
        m_loadedAssetBundles.TryGetValue(a_assetBundleName, out bundle);
        if (bundle == null)
        {
            return null;
        }

        // No dependencies are recorded, only the bundle itself is required.
        string[] dependencies = null;
        if (!m_dependencies.TryGetValue(a_assetBundleName, out dependencies))
        {
            return bundle;
        }

        // Make sure all dependencies are loaded
        foreach (var dependency in dependencies)
        {
            if (m_downloadingErrors.TryGetValue(a_assetBundleName, out a_error))
            {
                return bundle;
            }

            // Wait all the dependent assetBundles being loaded.
            LoadedAssetBundle dependentBundle;
            m_loadedAssetBundles.TryGetValue(dependency, out dependentBundle);
            if (dependentBundle == null)
            {
                return null;
            }
        }

        return bundle;
    }

    // Load AssetBundleManifest.
    public AssetBundleLoadManifestOperation Initialize(string a_manifestAssetBundleName)
    {
        if (IsInitialized())
            return null;

#if UNITY_EDITOR
        // If we're in Editor simulation mode, we don't need the manifest assetBundle.
        if (SimulateAssetBundleInEditor)
        {
            return null;
        }
#endif

        LoadAssetBundle(a_manifestAssetBundleName, true);
        var operation = new AssetBundleLoadManifestOperation(a_manifestAssetBundleName, "AssetBundleManifest", typeof (AssetBundleManifest));
        m_inProgressOperationsByName.Add(a_manifestAssetBundleName,operation);
        m_inProgressOperations.Add(new KeyValuePair<string, AssetBundleLoadOperation>(a_manifestAssetBundleName, operation));
        return operation;
    }
        
    // Load AssetBundle and its dependencies.
    public void LoadAssetBundle(string a_assetBundleName, bool a_isLoadingAssetBundleManifest = false)
    {
#if UNITY_EDITOR
        // If we're in Editor simulation mode, we don't have to really load the assetBundle and its dependencies.
        if (SimulateAssetBundleInEditor)
        {
            return;
        }
#endif

        if (!a_isLoadingAssetBundleManifest)
        {
            a_assetBundleName = RemapVariantName(a_assetBundleName);
        }

        // Check if the assetBundle has already been processed.
        var isAlreadyProcessed = LoadAssetBundleInternal(a_assetBundleName, a_isLoadingAssetBundleManifest);

        // Load dependencies.
        if (!isAlreadyProcessed && !a_isLoadingAssetBundleManifest)
        {
            LoadDependencies(a_assetBundleName);
        }
    }

    // Remaps the asset bundle name to the best fitting asset bundle variant.
    protected string RemapVariantName(string a_assetBundleName)
    {
        var bundlesWithVariant = m_assetBundleManifest.GetAllAssetBundlesWithVariant();

        // If the asset bundle doesn't have variant, simply return.
        if (Array.IndexOf(bundlesWithVariant, a_assetBundleName) < 0)
        {
            return a_assetBundleName;
        }

        var split = a_assetBundleName.Split('.');

        var bestFit = int.MaxValue;
        var bestFitIndex = -1;
        // Loop all the assetBundles with variant to find the best fit variant assetBundle.
        for (var i = 0; i < bundlesWithVariant.Length; i++)
        {
            var curSplit = bundlesWithVariant[i].Split('.');
            if (curSplit[0] != split[0])
            {
                continue;
            }

            var found = Array.IndexOf(m_variants, curSplit[1]);
            if (found != -1 && found < bestFit)
            {
                bestFit = found;
                bestFitIndex = i;
            }
        }

        if (bestFitIndex != -1)
        {
            return bundlesWithVariant[bestFitIndex];
        }
        return a_assetBundleName;
    }

    // Where we actuall call WWW to download the assetBundle.
    protected bool LoadAssetBundleInternal(string a_assetBundleName, bool a_isLoadingAssetBundleManifest)
    {
        // Already loaded.
        LoadedAssetBundle bundle = null;
        m_loadedAssetBundles.TryGetValue(a_assetBundleName, out bundle);
        if (bundle != null)
        {
            bundle.m_ReferencedCount++;
            return true;
        }

        var url = m_baseDownloadingUrl + a_assetBundleName;

        AssetBundle newBundle = AssetBundle.LoadFromFile(url);

        if (newBundle == null)
        {
            if (!m_downloadingErrors.ContainsKey(a_assetBundleName))
                m_downloadingErrors.Add(a_assetBundleName, "Asset Bundle Not Found: " + url);

            return false;
        }

        LoadedAssetBundle newLoadedAssetBundle = new LoadedAssetBundle(newBundle);

        m_loadedAssetBundles.Add(a_assetBundleName, newLoadedAssetBundle);

        return false;

    }

    // Where we get all the dependencies and load them all.
    protected void LoadDependencies(string a_assetBundleName)
    {
        if (m_assetBundleManifest == null)
        {
            Debug.LogError("Please initialize AssetBundleManifest by calling AssetBundleManager.Initialize()");
            return;
        }

        // Get dependecies from the AssetBundleManifest object..
        var dependencies = m_assetBundleManifest.GetAllDependencies(a_assetBundleName);
        if (dependencies.Length == 0)
        {
            return;
        }

        for (var i = 0; i < dependencies.Length; i++)
        {
            dependencies[i] = RemapVariantName(dependencies[i]);
        }

        // Record and load all dependencies.
        m_dependencies.Add(a_assetBundleName, dependencies);
        for (var i = 0; i < dependencies.Length; i++)
        {
            LoadAssetBundleInternal(dependencies[i], false);
        }
    }

    // Unload assetbundle and its dependencies.
    public void UnloadAssetBundle(string a_assetBundleName)
    {
#if UNITY_EDITOR
        // If we're in Editor simulation mode, we don't have to load the manifest assetBundle.
        if (SimulateAssetBundleInEditor)
        {
            return;
        }
#endif

        //Debug.Log(m_LoadedAssetBundles.Count + " assetbundle(s) in memory before unloading " + assetBundleName);

        UnloadAssetBundleInternal(a_assetBundleName);
        UnloadDependencies(a_assetBundleName);

        //Debug.Log(m_LoadedAssetBundles.Count + " assetbundle(s) in memory after unloading " + assetBundleName);
    }

    protected void UnloadDependencies(string a_assetBundleName)
    {
        string[] dependencies = null;
        if (!m_dependencies.TryGetValue(a_assetBundleName, out dependencies))
        {
            return;
        }

        // Loop dependencies.
        foreach (var dependency in dependencies)
        {
            UnloadAssetBundleInternal(dependency);
        }

        m_dependencies.Remove(a_assetBundleName);
    }

    protected void UnloadAssetBundleInternal(string a_assetBundleName)
    {
        string error;
        var bundle = GetLoadedAssetBundle(a_assetBundleName, out error);
        if (bundle == null)
        {
            return;
        }

        if (--bundle.m_ReferencedCount == 0)
        {
            bundle.m_AssetBundle.Unload(false);
            m_loadedAssetBundles.Remove(a_assetBundleName);
            //Debug.Log("AssetBundle " + assetBundleName + " has been unloaded successfully");
        }
    }

    private void Update()
    {
        // Update all in progress operations
        for (int i = 0; i < m_inProgressOperations.Count; ++i)
        {
            var inProgressOperation = m_inProgressOperations[i];
            if (!inProgressOperation.Value.Update())
            {
                m_completedOperations.Add(inProgressOperation);
            }
        }

        //Remove the completed from the inProgress and add to the completed
        for (int i = 0; i < m_completedOperations.Count; ++i)
        {
            var completedOperation = m_completedOperations[i];
            m_inProgressOperationsByName.Remove(completedOperation.Key);
            m_inProgressOperations.Remove(completedOperation);     
        }

        m_completedOperations.Clear();
    }

    static string AssetToKey(string a_assetBundleName, string a_assetName)
    {
        return a_assetBundleName + ':' + a_assetName;
    }

    static string AllAssetsToKey(string a_assetBundleName, Type a_type)
    {
        return a_assetBundleName + ':' + a_type.Name + ':' + '*';
    }

#if UNITY_EDITOR
    static Dictionary<string, string[]> s_cachedPathsForBundle = new Dictionary<string, string[]>();
#endif

    // Load asset from the given assetBundle.
    public AssetBundleLoadAssetOperation LoadAssetAsync(string a_assetBundleName, string a_assetName, Type a_type)
    {
        if (string.IsNullOrEmpty(a_assetBundleName) || string.IsNullOrEmpty(a_assetName))
        {
            return null;
        }

        AssetBundleLoadAssetOperation operation = null;
        
#if UNITY_EDITOR
		if (SimulateAssetBundleInEditor)
		{
		    string[] paths = null;

            if(!s_cachedPathsForBundle.TryGetValue(a_assetBundleName, out paths))
            {
                paths = AssetDatabase.GetAssetPathsFromAssetBundle(a_assetBundleName);
                s_cachedPathsForBundle.Add(a_assetBundleName, paths);
            }

            //var assetPaths = AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(a_assetBundleName, a_assetName);
			if (!paths.Contains(a_assetName))
            {
                Debug.LogError("There is no asset with name \"" + a_assetName + "\" in " + a_assetBundleName);
                return null;
            }

            var target = AssetDatabase.LoadAssetAtPath(a_assetName, a_type);
            operation = new AssetBundleLoadAssetOperationSimulation(target);
        }
        else
#endif
        {
            var key = AssetToKey(a_assetBundleName, a_assetName);

            AssetBundleLoadOperation inProgressOperation;
            if (m_inProgressOperationsByName.TryGetValue(key, out inProgressOperation))
            {
                if (inProgressOperation is AssetBundleLoadAssetOperation)
                {
                    return inProgressOperation as AssetBundleLoadAssetOperation;
                }
            }

            LoadAssetBundle(a_assetBundleName);
            operation = new AssetBundleLoadAssetOperationFull(a_assetBundleName, a_assetName, a_type);

            //Debug.Log("Adding operation " + a_assetName);

            m_inProgressOperationsByName.Add(key, operation);
            m_inProgressOperations.Add(new KeyValuePair<string, AssetBundleLoadOperation>(key, operation));
        }

        return operation;
    }

    public T LoadAsset<T>(string a_assetBundleName, string a_assetName) where T : UnityEngine.Object
    {
        var loadType = typeof (T);
        if (typeof (Component).IsAssignableFrom(loadType))
        {
            var go = LoadAsset(a_assetBundleName, a_assetName, typeof(GameObject)) as GameObject;
            if (go != null)
            {
                return go.GetComponent(loadType) as T;
            }

            return null;
        }
         
        return LoadAsset(a_assetBundleName, a_assetName, loadType) as T;
    }

    private UnityEngine.Object LoadAsset(string a_assetBundleName, string a_assetName, Type a_type)
    {
        if (string.IsNullOrEmpty(a_assetBundleName) || string.IsNullOrEmpty(a_assetName))
        {
            return null;
        }

#if UNITY_EDITOR
        if (SimulateAssetBundleInEditor)
        {
            string[] paths = null;

            if (!s_cachedPathsForBundle.TryGetValue(a_assetBundleName, out paths))
            {
                paths = AssetDatabase.GetAssetPathsFromAssetBundle(a_assetBundleName);
                s_cachedPathsForBundle.Add(a_assetBundleName, paths);
            }

            if (!paths.Contains(a_assetName))
            {
                Debug.LogError("There is no asset with name \"" + a_assetName + "\" in " + a_assetBundleName);
                return null;
            }

            var target = AssetDatabase.LoadAssetAtPath(a_assetName, a_type);
            return target;
        }
        else
#endif
        {
            LoadAssetBundle(a_assetBundleName);

            var downloadingError = string.Empty;
            var bundle = GetLoadedAssetBundle(a_assetBundleName, out downloadingError);
            if (bundle == null)
            {
                Debug.LogError("The asset bundle wasn't loaded");
                return null;
            }

            return bundle.m_AssetBundle.LoadAsset(a_assetName, a_type);
        }
    }

    // Load asset from the given assetBundle.
    public AssetBundleLoadAllAssetsOperation LoadAllAssetsAsync(string a_assetBundleName, Type a_type)
    {
        if (string.IsNullOrEmpty(a_assetBundleName))
        {
            return null;
        }

        AssetBundleLoadAllAssetsOperation operation = null;

#if UNITY_EDITOR
        if (SimulateAssetBundleInEditor)
        {
            var paths = AssetDatabase.GetAssetPathsFromAssetBundle(a_assetBundleName);
            var targets = new List<UnityEngine.Object>(paths.Length);
            for (int i = 0; i < paths.Length; ++i)
            {
                var path = paths[i];
                var target = AssetDatabase.LoadAssetAtPath(path, a_type);
                targets.Add(target);
                paths[i] = path.ToLower();
            }
            operation = new AssetBundleLoadAllAssetsOperationSimulation(paths, targets.ToArray());
        }
        else
#endif
        {
            var key = AllAssetsToKey(a_assetBundleName, a_type);

            AssetBundleLoadOperation inProgressOperation;
            if (m_inProgressOperationsByName.TryGetValue(key, out inProgressOperation))
            {
                if (inProgressOperation is AssetBundleLoadAllAssetsOperation)
                {
                    return inProgressOperation as AssetBundleLoadAllAssetsOperation;
                }
            }

            LoadAssetBundle(a_assetBundleName);
            operation = new AssetBundleLoadAllAssetsOperationFull(a_assetBundleName, a_type);

            m_inProgressOperationsByName.Add(key, operation);
            m_inProgressOperations.Add(new KeyValuePair<string, AssetBundleLoadOperation>(key, operation));
        }

        return operation;
    }

    public UnityEngine.Object[] LoadAllAssets(string a_assetBundleName, Type a_type)
    {
        if (string.IsNullOrEmpty(a_assetBundleName))
        {
            return null;
        }

#if UNITY_EDITOR
        if (SimulateAssetBundleInEditor)
        {
            var paths = AssetDatabase.GetAssetPathsFromAssetBundle(a_assetBundleName);
            var targets = new UnityEngine.Object[paths.Length];
            for (int i = 0; i < paths.Length; ++i)
            {
                var path = paths[i];
                var target = AssetDatabase.LoadAssetAtPath(path, a_type);
                targets[i]=target;
                paths[i] = path.ToLower();
            }
            return targets;
        }
        else
#endif
        {
            LoadAssetBundle(a_assetBundleName);

            var downloadingError = string.Empty;
            var bundle = GetLoadedAssetBundle(a_assetBundleName, out downloadingError);
            if (bundle == null)
            {
                Debug.LogError("The asset bundle wasn't loaded");
                return null;
            }

            return bundle.m_AssetBundle.LoadAllAssets(a_type);
        }
    }

    // Load level from the given assetBundle.
    public AssetBundleLoadOperation LoadLevelAsync(string a_assetBundleName, string a_levelName, bool a_isAdditive, bool a_allowSceneActivation)
    {
        AssetBundleLoadOperation operation = null;
#if UNITY_EDITOR
        if (SimulateAssetBundleInEditor)
        {
            var levelPaths = AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(a_assetBundleName, a_levelName);
            if (levelPaths.Length == 0)
            {
                ///@TODO: The error needs to differentiate that an asset bundle name doesn't exist
                //        from that there right scene does not exist in the asset bundle...

                Debug.LogError("There is no scene with name \"" + a_levelName + "\" in " + a_assetBundleName);
                return null;
            }

            if (a_isAdditive)
            {
                EditorApplication.LoadLevelAdditiveInPlayMode(levelPaths[0]);
            }
            else
            {
                EditorApplication.LoadLevelInPlayMode(levelPaths[0]);
            }

            operation = new AssetBundleLoadLevelSimulationOperation();
        }
        else
#endif
        {
            var key = AssetToKey(a_assetBundleName, a_levelName);

            AssetBundleLoadOperation inProgressOperation;
            if (m_inProgressOperationsByName.TryGetValue(key, out inProgressOperation))
            {
                var loadLevelOperation = inProgressOperation as AssetBundleLoadLevelOperation;
                if (loadLevelOperation != null)
                {
                    return loadLevelOperation;
                }
            }

            LoadAssetBundle(a_assetBundleName);
            operation = new AssetBundleLoadLevelOperation(a_assetBundleName, a_levelName, a_isAdditive, a_allowSceneActivation);

            m_inProgressOperationsByName.Add(key, operation);
            m_inProgressOperations.Add(new KeyValuePair<string, AssetBundleLoadOperation>(key, operation));
        }

        return operation;
    }

#if UNITY_EDITOR
    private static int m_simulateAssetBundleInEditor = -1;
    private const string kSimulateAssetBundles = "SimulateAssetBundles";
#endif

#region Other Variables

#endregion

#region Properties

    public static AssetBundleManager Instance { get; private set; }


    // The base downloading url which is used to generate the full downloading url with the assetBundle names.
    public string BaseDownloadingURL
    {
        get { return m_baseDownloadingUrl; }
        set { m_baseDownloadingUrl = value; }
    }

    // Variants which is used to define the active variants.
    public string[] Variants
    {
        get { return m_variants; }
        set { m_variants = value; }
    }

    // AssetBundleManifest object which can be used to load the dependecies and check suitable assetBundle variants.
    public AssetBundleManifest AssetBundleManifestObject
    {
        set { m_assetBundleManifest = value; }
    }

#if UNITY_EDITOR
    // Flag to indicate if we want to simulate assetBundles in Editor without building them actually.
    static public bool SimulateAssetBundleInEditor
    {
        get
        {
            if (m_simulateAssetBundleInEditor == -1)
            {
                m_simulateAssetBundleInEditor = EditorPrefs.GetBool(kSimulateAssetBundles, true) ? 1 : 0;
            }

            return m_simulateAssetBundleInEditor != 0;
        }
        set
        {
            var newValue = value ? 1 : 0;
            if (newValue != m_simulateAssetBundleInEditor)
            {
                m_simulateAssetBundleInEditor = newValue;
                EditorPrefs.SetBool(kSimulateAssetBundles, value);
            }
        }
    }

    public static string RefFromPrefab(UnityEngine.Object a_object)
    {
        string reference = string.Empty;

        var prefabType = PrefabUtility.GetPrefabType(a_object);
        if (prefabType == PrefabType.Prefab)
        {
            string path = AssetDatabase.GetAssetPath(a_object);
            var assetImporter = AssetImporter.GetAtPath(path);
            reference = BundleAndAssetToRef(assetImporter.assetBundleName, path, AssetDatabase.AssetPathToGUID(path));
        }

        return reference;
    }

#endif

    #endregion
} // End of AssetBundleManager.
