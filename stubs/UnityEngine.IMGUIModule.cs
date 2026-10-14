// Stub de compilation uniquement (voir UnityEngine.CoreModule.cs).
namespace UnityEngine
{
    public class GUILayoutOption { }

    public class GUI
    {
        public delegate void WindowFunction(int id);
        public static void DragWindow() { }
    }

    public class GUILayout
    {
        public static Rect Window(int id, Rect screenRect, GUI.WindowFunction func, string text, params GUILayoutOption[] options) { return screenRect; }
        public static void Label(string text, params GUILayoutOption[] options) { }
        public static bool Button(string text, params GUILayoutOption[] options) { return false; }
        public static bool Toggle(bool value, string text, params GUILayoutOption[] options) { return value; }
        public static int Toolbar(int selected, string[] texts, params GUILayoutOption[] options) { return selected; }
        public static Vector2 BeginScrollView(Vector2 scrollPosition, params GUILayoutOption[] options) { return scrollPosition; }
        public static void EndScrollView() { }
        public static void BeginHorizontal(params GUILayoutOption[] options) { }
        public static void EndHorizontal() { }
        public static void BeginVertical(params GUILayoutOption[] options) { }
        public static void EndVertical() { }
        public static void FlexibleSpace() { }
        public static void Space(float pixels) { }
        public static GUILayoutOption Width(float width) { return null; }
        public static GUILayoutOption Height(float height) { return null; }
        public static GUILayoutOption ExpandWidth(bool expand) { return null; }
    }
}
