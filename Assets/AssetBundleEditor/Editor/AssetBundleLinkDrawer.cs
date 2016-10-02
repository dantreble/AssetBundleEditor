using System;
using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(AssetBundleLinkAttribute))]
public class AssetBundleLinkDrawer : PropertyDrawer 
{
    public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
	{
		var baseHeight = base.GetPropertyHeight(prop, label);

		var linkAttribute = attribute as AssetBundleLinkAttribute;

		if(linkAttribute.type == typeof(Texture))
		{
			return baseHeight * 4.0f;
		}
		else
		{
			return baseHeight;
		}
	}
	
	public override void OnGUI (Rect pos, SerializedProperty prop, GUIContent label)
	{
		var linkAttribute = attribute as AssetBundleLinkAttribute;

		UnityEngine.Object currentObject = null;
		string updatedRef = prop.stringValue;

		if(!string.IsNullOrEmpty(prop.stringValue))
		{
			string assetGUID;
			string bundle;
			string assetPath;

			AssetBundleManager.RefToBundleAssetGUID(prop.stringValue, out bundle, out assetPath, out assetGUID);

			assetPath = string.IsNullOrEmpty(assetGUID) ? assetPath : AssetDatabase.GUIDToAssetPath(assetGUID);

			currentObject = AssetDatabase.LoadAssetAtPath(assetPath, linkAttribute.type);

            bundle = AssetBundleManager.GetBundleName(assetPath);

			updatedRef = AssetBundleManager.BundleAndAssetToRef(bundle,assetPath,AssetDatabase.AssetPathToGUID(assetPath));
		}

		bool needsFixing = updatedRef != prop.stringValue;

		float fixButtonWidth = needsFixing ? Mathf.Min (35f, pos.width / 2f) : 0f;

		Rect left = new Rect(pos.x, pos.y, pos.width - fixButtonWidth, pos.height);

		var newObject = EditorGUI.ObjectField( left, label, currentObject, linkAttribute.type, false);

		if(needsFixing)
		{
			Rect right = new Rect(pos.x + pos.width - fixButtonWidth, pos.y , fixButtonWidth, pos.height);

			var restoreColor = GUI.color;
			GUI.color = Color.red;
			if(GUI.Button(right,"Fix!"))
			{
				prop.stringValue = updatedRef;
			}

            GUI.color = restoreColor;
		}

		if (newObject != currentObject)
		{
			var path = AssetDatabase.GetAssetPath(newObject);

			if(string.IsNullOrEmpty(path))
			{
				prop.stringValue = null;
			}
			else
			{
                var assetBundleName = AssetBundleManager.GetBundleName(path);
                if (string.IsNullOrEmpty(assetBundleName))
                {
                    var assetImporter = AssetImporter.GetAtPath(path);
                    if (assetImporter != null)
                    {
                        var bundleName = string.Format(linkAttribute.defaultBundle, newObject.name);

                        Debug.LogWarning("Assigning asset to bundle: " + bundleName);

                        assetImporter.assetBundleName = bundleName;
                        assetImporter.SaveAndReimport();
                    }
                }

				prop.stringValue = AssetBundleManager.BundleAndAssetToRef(assetBundleName,path,AssetDatabase.AssetPathToGUID(path));
			}
		}
	}   

}
