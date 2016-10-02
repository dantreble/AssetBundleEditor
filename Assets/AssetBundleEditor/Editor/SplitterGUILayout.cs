using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Reflection;

public class SplitterState
{
    public object Object;

    private FieldInfo m_realSizesField;
    public int[] realSizes
    {
        get
        {
            if (m_realSizesField == null)
            {
                m_realSizesField = Object.GetType().GetField("realSizes");
            }
            return (int[])m_realSizesField.GetValue(Object);
        }
    }

    public SplitterState(params float[] a_relativeSizes)
    {
        Init(a_relativeSizes, null, null, 0);
    }

    public SplitterState(float[] a_relativeSizes, int[] a_minSizes, int[] a_maxSizes)
    {
        Init(a_relativeSizes, a_minSizes, a_maxSizes, 0);
    }

    public SplitterState(float[] a_relativeSizes, int[] a_minSizes, int[] a_maxSizes, int a_splitSize)
    {
        Init(a_relativeSizes, a_minSizes, a_maxSizes, a_splitSize);
    }

    private void Init(float[] a_relativeSizes, int[] a_minSizes, int[] a_maxSizes, int a_splitSize)
    {
        var assembly = Assembly.GetAssembly(typeof(EditorGUI));
        var type = assembly.GetType("UnityEditor.SplitterState");
        Object = System.Activator.CreateInstance(type, new object[] { a_relativeSizes, a_minSizes, a_maxSizes, a_splitSize });
    }
}
 
public static class SplitterGUILayout
{
    private static Assembly m_assembly;
    private static MethodInfo m_beginHorizontalSplitMethod;
    private static MethodInfo m_beginHorizontalSplitWithStyleMethod;
    private static MethodInfo m_beginVerticalSplitMethod;
    private static MethodInfo m_beginVerticalSplitWithStyleMethod;
    private static MethodInfo m_endHorizontalSplitMethod;
    private static MethodInfo m_endVerticalSplitMethod;

    public static void BeginHorizontalSplit(SplitterState a_state, params GUILayoutOption[] a_options)
    {
        Init();
        m_beginHorizontalSplitMethod.Invoke(null, new object[] { a_state.Object, a_options });
    }

    public static void BeginHorizontalSplit(SplitterState a_state, GUIStyle a_style, params GUILayoutOption[] a_options)
    {
        Init();
        m_beginHorizontalSplitWithStyleMethod.Invoke(null, new object[] { a_state.Object, a_style, a_options });
    }

    public static void BeginVerticalSplit(SplitterState a_state, params GUILayoutOption[] a_options)
    {
        Init();
        m_beginVerticalSplitMethod.Invoke(null, new object[] { a_state.Object, a_options });
    }

    public static void BeginVerticalSplit(SplitterState a_state, GUIStyle a_style, params GUILayoutOption[] a_options)
    {
        Init();
        m_beginVerticalSplitWithStyleMethod.Invoke(null, new object[] { a_state.Object, a_style, a_options });
    }

    public static void EndHorizontalSplit()
    {
        Init();
        m_endHorizontalSplitMethod.Invoke(null, null);
    }

    public static void EndVerticalSplit()
    {
        Init();
        m_endVerticalSplitMethod.Invoke(null, null);
    }

    private static void Init()
    {
        if (m_assembly == null)
        {
            m_assembly = Assembly.GetAssembly(typeof(EditorGUI));
            var type = m_assembly.GetType("UnityEditor.SplitterGUILayout");
            var typeMethods = type.GetMethods();
            var beginHorizontalSplitMethods = typeMethods.Where(m => m.Name == "BeginHorizontalSplit");
            m_beginHorizontalSplitMethod = beginHorizontalSplitMethods.First();
            m_beginHorizontalSplitWithStyleMethod = beginHorizontalSplitMethods.Last();
            var beginVerticalSplitMethods = typeMethods.Where(m => m.Name == "BeginVerticalSplit");
            m_beginVerticalSplitMethod = beginVerticalSplitMethods.First();
            m_beginVerticalSplitWithStyleMethod = beginVerticalSplitMethods.Last();
            m_endHorizontalSplitMethod = typeMethods.First(m => m.Name == "EndHorizontalSplit");
            m_endVerticalSplitMethod = typeMethods.First(m => m.Name == "EndVerticalSplit");
        }
    }
}