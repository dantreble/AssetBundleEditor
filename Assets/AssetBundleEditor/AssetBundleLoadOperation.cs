using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

public abstract class AssetBundleLoadOperation : IEnumerator
{
	public object Current
	{
		get
		{
			return null;
		}
	}
	public bool MoveNext()
	{
		return !IsDone();
	}
	
	public void Reset()
	{
	}
	
	abstract public bool Update ();
	
	abstract public bool IsDone ();
}

public class AssetBundleLoadLevelSimulationOperation : AssetBundleLoadOperation
{	
	public AssetBundleLoadLevelSimulationOperation ()
	{
	}
	
	public override bool Update ()
	{
		return false;
	}
	
	public override bool IsDone ()
	{		
		return true;
	}
}

public class AssetBundleLoadLevelOperation : AssetBundleLoadOperation
{
	protected string 				m_AssetBundleName;
	protected string 				m_LevelName;
	protected bool 						m_IsAdditive;
    protected bool m_allowSceneActivation;
    protected string 				m_DownloadingError;
	protected AsyncOperation		m_Request;

	public AssetBundleLoadLevelOperation (string assetbundleName, string levelName, bool isAdditive, bool allowSceneActivation)
	{
		m_AssetBundleName = assetbundleName;
		m_LevelName = levelName;
		m_IsAdditive = isAdditive;
	    m_allowSceneActivation = allowSceneActivation;
	}

    public AsyncOperation Request
    {
        get { return m_Request; }
    }

    public bool AllowSceneActivation
    {
        get { return m_allowSceneActivation; }
        set
        {
            m_allowSceneActivation = value;

            if (m_Request != null)
            {
                m_Request.allowSceneActivation = value;
            }
        }
    }

    public override bool Update ()
	{
        if (m_Request != null)
        {
            return false;
        }

        var bundle = AssetBundleManager.Instance.GetLoadedAssetBundle (m_AssetBundleName, out m_DownloadingError);

        if (bundle == null)
        {
            return true;
        }

        m_Request = SceneManager.LoadSceneAsync(m_LevelName, m_IsAdditive ? LoadSceneMode.Additive : LoadSceneMode.Single);

        if (m_Request != null)
        {
            m_Request.allowSceneActivation = m_allowSceneActivation;
        }

        return false;
	}
	
	public override bool IsDone ()
	{
		// Return if meeting downloading error.
		// m_DownloadingError might come from the dependency downloading.
		if (m_Request == null && m_DownloadingError != null)
		{
			Debug.LogError(m_DownloadingError);
			return true;
		}
		
		return m_Request != null && m_Request.isDone;
	}
}

public abstract class AssetBundleLoadAssetOperation : AssetBundleLoadOperation
{
	public abstract T GetAsset<T>() where T : UnityEngine.Object;
}

public class AssetBundleLoadAssetOperationSimulation : AssetBundleLoadAssetOperation
{
	Object							m_SimulatedObject;
	
	public AssetBundleLoadAssetOperationSimulation (Object simulatedObject)
	{
		m_SimulatedObject = simulatedObject;
	}
	
	public override T GetAsset<T>()
	{
		return m_SimulatedObject as T;
	}
	
	public override bool Update ()
	{
		return false;
	}
	
	public override bool IsDone ()
	{
		return true;
	}
}

public class AssetBundleLoadAssetOperationFull : AssetBundleLoadAssetOperation
{
	protected string 				m_AssetBundleName;
	protected string 				m_AssetName;
	protected string 				m_DownloadingError;
	protected System.Type 			m_Type;
	protected AssetBundleRequest	m_Request = null;

	public AssetBundleLoadAssetOperationFull (string bundleName, string assetName, System.Type type)
	{
		m_AssetBundleName = bundleName;
		m_AssetName = assetName;
		m_Type = type;
	}
	
	public override T GetAsset<T>()
	{
		if (m_Request != null && m_Request.asset != null)
			return m_Request.asset as T;
		else
			return null;
	}
	
	// Returns true if more Update calls are required.
	public override bool Update ()
	{
		if (m_Request != null)
			return false;

		LoadedAssetBundle bundle = AssetBundleManager.Instance.GetLoadedAssetBundle (m_AssetBundleName, out m_DownloadingError);
		if (bundle != null)
		{
			m_Request = bundle.m_AssetBundle.LoadAssetAsync (m_AssetName, m_Type);

		    //if (m_Request != null)
		    //{
            //    Debug.Log("Assigning request for " + m_AssetName);    
		    //}
		    //else
		    //{
            //    Debug.Log("NULL request for " + m_AssetName);    
		    //}
            
			return false;
		}
		else
		{
            Debug.Log("NULL bundle for " + m_AssetName);    
			return true;
		}
	}
	
	public override bool IsDone ()
	{
		// Return if meeting downloading error.
		// m_DownloadingError might come from the dependency downloading.
		if (m_Request == null && m_DownloadingError != null)
		{
			Debug.LogError(m_DownloadingError);
			return true;
		}

		return m_Request != null && m_Request.asset != null;
	}
}

public abstract class AssetBundleLoadAllAssetsOperation : AssetBundleLoadOperation
{
    public abstract string[] GetAllAssetPaths();
	public abstract T[] GetAllAssets<T>() where T : UnityEngine.Object;
}

public class AssetBundleLoadAllAssetsOperationSimulation : AssetBundleLoadAllAssetsOperation
{
    string[] m_simulatedPaths;
    Object[] m_simulatedObjects;

    public AssetBundleLoadAllAssetsOperationSimulation(string[] a_simulatedPaths, Object[] a_simulatedObjects)
	{
        m_simulatedPaths = a_simulatedPaths;
        m_simulatedObjects = a_simulatedObjects;
	}

    public override string[] GetAllAssetPaths()
    {
        return m_simulatedPaths;
    }
	
	public override T[] GetAllAssets<T>()
	{
#if UNITY_EDITOR
        return System.Array.ConvertAll(m_simulatedObjects, a => (T)a);
#else
        throw new System.NotSupportedException();
#endif
	}
	
	public override bool Update ()
	{
		return false;
	}
	
	public override bool IsDone ()
	{
		return true;
	}
}

public class AssetBundleLoadAllAssetsOperationFull : AssetBundleLoadAllAssetsOperation
{
	protected string 				m_AssetBundleName;
	protected string 				m_DownloadingError;
	protected System.Type 			m_Type;
	protected AssetBundleRequest	m_Request = null;
    protected string[] m_paths;

    public AssetBundleLoadAllAssetsOperationFull(string bundleName, System.Type type)
	{
		m_AssetBundleName = bundleName;
		m_Type = type;
	}

    public override string[] GetAllAssetPaths()
    {
        return m_paths;
    }
	
	public override T[] GetAllAssets<T>()
	{
		if (m_Request != null && m_Request.allAssets != null)
			return System.Array.ConvertAll(m_Request.allAssets, a => (T)a);
		else
			return null;
	}
	
	// Returns true if more Update calls are required.
	public override bool Update ()
	{
		if (m_Request != null)
			return false;

		LoadedAssetBundle bundle = AssetBundleManager.Instance.GetLoadedAssetBundle (m_AssetBundleName, out m_DownloadingError);
		if (bundle != null)
		{
            m_paths = bundle.m_AssetBundle.GetAllAssetNames();
            for (int i = 0; i < m_paths.Length; ++i)
            {
                m_paths[i] = m_paths[i].ToLower();
            }
			m_Request = bundle.m_AssetBundle.LoadAllAssetsAsync (m_Type);
			return false;
		}
		else
		{
			return true;
		}
	}
	
	public override bool IsDone ()
	{
		// Return if meeting downloading error.
		// m_DownloadingError might come from the dependency downloading.
		if (m_Request == null && m_DownloadingError != null)
		{
			Debug.LogError(m_DownloadingError);
			return true;
		}

		return m_Request != null && m_Request.allAssets != null;
	}
}

public class AssetBundleLoadManifestOperation : AssetBundleLoadAssetOperationFull
{
	public AssetBundleLoadManifestOperation (string bundleName, string assetName, System.Type type)
		: base(bundleName, assetName, type)
	{
	}

	public override bool Update ()
	{
		base.Update();
		
		if (m_Request != null && m_Request.isDone)
		{
			AssetBundleManager.Instance.AssetBundleManifestObject = GetAsset<AssetBundleManifest>();
			return false;
		}
		else
			return true;
	}
}

