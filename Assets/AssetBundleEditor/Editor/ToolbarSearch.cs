using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Reflection;

public class ToolbarSearch
{
    private static GUIStyle m_textFieldStyle;
    private static GUIStyle m_cancelButtonStyle;
    private GUIContent m_content = new GUIContent();
    private string m_searchString = string.Empty;
    private string[] m_searchStrings;
    private bool m_focus;

    public string Text
    {
        get { return m_searchString; }
        set
        {
            m_searchString = value;
            if (!string.IsNullOrEmpty(m_searchString))
            {
                m_searchStrings = m_searchString.ToLower().Split(' ').Where(s => !string.IsNullOrEmpty(s)).ToArray();
            }
            else
            {
                m_searchStrings = null;
            }
        }
    }

    private static GUIStyle TextFieldStyle
    {
        get
        {
            if (m_textFieldStyle == null)
            {
                m_textFieldStyle = new GUIStyle("ToolbarSeachTextField");
            }
            return m_textFieldStyle;
        }
    }

    private static GUIStyle CancelButtonStyle
    {
        get
        {
            if (m_cancelButtonStyle == null)
            {
                m_cancelButtonStyle = new GUIStyle("ToolbarSeachCancelButton");
            }
            return m_cancelButtonStyle;
        }
    }

    public bool OnGUI()
    {
        return OnGUI(GUILayout.Width(200f));
    }

    public bool OnGUI(params GUILayoutOption[] a_options)
    {
        bool result = false;

        GUILayout.BeginHorizontal();
        {
            m_content.text = m_searchString;

            var searchRect = GUILayoutUtility.GetRect(m_content, TextFieldStyle, a_options);
            var cancelRect = GUILayoutUtility.GetRect(GUIContent.none, CancelButtonStyle);
            var combinedRect = new Rect(searchRect.x, searchRect.y, searchRect.width + cancelRect.width, searchRect.height);

            result = OnGUI(combinedRect);
        }
        GUILayout.EndHorizontal();

        return result;
    }

    public bool OnGUI(Rect a_position)
    {
        bool result = false;

        GUI.SetNextControlName("toolbar search");

        var searchRect = new Rect(a_position.x, a_position.y, a_position.width - 14f, a_position.height);
        var cancelRect = new Rect(searchRect.xMax, a_position.y, 14f, a_position.height);

        var searchString = GUI.TextField(searchRect, m_searchString, TextFieldStyle);
        if (searchString != m_searchString)
        {
            Text = searchString;
            result = true;
        }
        if (GUI.Button(cancelRect, GUIContent.none, CancelButtonStyle) && !string.IsNullOrEmpty(m_searchString))
        {
            Text = string.Empty;
            GUIUtility.keyboardControl = 0;
            result = true;
        }

        if (m_focus)
        {
            if (Event.current.type == EventType.Repaint)
            {
                m_focus = false;
            }

            EditorGUI.FocusTextInControl("toolbar search");
        }

        return result;
    }

    public bool Check(string a_name)
    {
        return StringUtils.Search(m_searchStrings, a_name);
    }

    public void Focus()
    {
        m_focus = true;
    }
}
