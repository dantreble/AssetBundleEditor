using UnityEngine;
using System;
using System.Collections;

public class AssetBundleLinkAttribute : PropertyAttribute
{
    public readonly Type type;
	public readonly string defaultBundle;
    public AssetBundleLinkAttribute(Type type, string defaultBundle=null)
    {
        this.type = type;
		this.defaultBundle = defaultBundle;
    }
}
