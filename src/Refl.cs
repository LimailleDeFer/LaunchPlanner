using System;
using System.Collections;
using System.Reflection;

namespace LaunchPlanner
{
    /// <summary>
    /// Reads KSP members through reflection when we do not want to depend on their exact form
    /// (field or property). If a member does not exist, null is returned instead of crashing.
    /// </summary>
    internal static class Refl
    {
        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static object Get(object target, string name)
        {
            if (target == null) return null;
            return Read(target.GetType(), target, name, Inst);
        }

        public static object GetStatic(Type type, string name)
        {
            return Read(type, null, name, Stat);
        }

        static object Read(Type type, object target, string name, BindingFlags flags)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, flags | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(target);
                PropertyInfo p = t.GetProperty(name, flags | BindingFlags.DeclaredOnly);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(target, null);
            }
            return null;
        }

        public static string Str(object target, string name)
        {
            object v = Get(target, name);
            return v == null ? "" : v.ToString();
        }

        public static double Num(object target, string name)
        {
            object v = Get(target, name);
            if (v == null) return 0;
            try { return Convert.ToDouble(v); } catch { return 0; }
        }

        public static bool Bool(object target, string name)
        {
            object v = Get(target, name);
            return v is bool && (bool)v;
        }

        public static object Call(object target, string method, params object[] args)
        {
            if (target == null) return null;
            MethodInfo m = target.GetType().GetMethod(method, Inst, null, TypesOf(args), null);
            return m == null ? null : m.Invoke(target, args);
        }

        static Type[] TypesOf(object[] args)
        {
            Type[] types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++) types[i] = args[i].GetType();
            return types;
        }

        /// <summary>Iterates a collection, whether enumerable or only indexable (Count + this[int]).</summary>
        public static IEnumerable Items(object collection)
        {
            if (collection == null) yield break;
            IEnumerable e = collection as IEnumerable;
            if (e != null)
            {
                foreach (object o in e) yield return o;
                yield break;
            }
            object countObj = Get(collection, "Count");
            PropertyInfo indexer = collection.GetType().GetProperty("Item", new Type[] { typeof(int) });
            if (countObj == null || indexer == null) yield break;
            int count = Convert.ToInt32(countObj);
            for (int i = 0; i < count; i++) yield return indexer.GetValue(collection, new object[] { i });
        }
    }
}
