#region Using

using UnityEngine;
using System.Collections;
using System.Linq;

#endregion


public static class StringUtils
{
    #region Types
    #endregion


    #region Properties
    #endregion


    #region Methods

    public static bool Search(string[] a_searchStrings, string a_value)
    {
        if (a_searchStrings != null && a_searchStrings.Length > 0)
        {
            var value = a_value.ToLower();
            foreach (var searchString in a_searchStrings)
            {
                var search = searchString;
                bool exclude = searchString.First() == '-';
                if (exclude)
                {
                    search = searchString.Substring(1);
                }

                bool contains = !string.IsNullOrEmpty(search) && value.Contains(search);
                if (contains == exclude)
                {
                    return false;
                }
            }
        }

        return true;
    }

    #endregion
}
