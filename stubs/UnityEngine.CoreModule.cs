// Stub de compilation uniquement : reproduit la petite partie de l'API Unity 2019.4
// utilisée par le mod. Ce fichier n'est jamais livré ; en jeu, c'est la vraie DLL de KSP
// qui est chargée. Les noms, signatures et assemblies doivent correspondre exactement.
using System;

namespace UnityEngine
{
    public class Object
    {
        public static void Destroy(Object obj) { }
        public static void DontDestroyOnLoad(Object target) { }
    }

    public class Component : Object { }
    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }

    public class Texture : Object { }

    public class Texture2D : Texture
    {
        public Texture2D(int width, int height) { }
        public void SetPixel(int x, int y, Color color) { }
        public void Apply() { }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { }
        public float x { get { return 0; } set { } }
        public float y { get { return 0; } set { } }
        public float width { get { return 0; } set { } }
        public float height { get { return 0; } set { } }
    }

    public static class Screen
    {
        public static int width { get { return 0; } }
        public static int height { get { return 0; } }
    }

    public class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
        public static void LogException(Exception exception) { }
    }
}
