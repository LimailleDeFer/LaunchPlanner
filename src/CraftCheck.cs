using System;
using System.Collections.Generic;
using UnityEngine;

namespace LaunchPlanner
{
    /// <summary>
    /// Check of the vessel once it is loaded in the editor, on the parts' real colliders: whatever the part mods, two
    /// parts of the launcher that sink into each other (a leg inside its tank, two thrusters in the same place) are
    /// reported, in the build notes and in KSP.log.
    /// </summary>
    public static class CraftCheck
    {
        const float Tolerance = 0.08f;   // m: parts that merely touch, or a collider a little larger than its model

        public static List<string> Overlaps(List<Part> parts, HashSet<uint> launcher)
        {
            List<string> found = new List<string>();
            List<KeyValuePair<Part, Collider[]>> shapes = new List<KeyValuePair<Part, Collider[]>>();
            foreach (Part p in parts)
            {
                if (p == null) continue;
                List<Collider> usable = new List<Collider>();
                foreach (Collider c in p.GetComponentsInChildren<Collider>(false))
                    if (c != null && c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy) usable.Add(c);
                if (usable.Count > 0) shapes.Add(new KeyValuePair<Part, Collider[]>(p, usable.ToArray()));
            }
            for (int i = 0; i < shapes.Count; i++)
                for (int j = i + 1; j < shapes.Count; j++)
                {
                    Part a = shapes[i].Key, b = shapes[j].Key;
                    if (launcher.Count > 0 && !launcher.Contains(a.persistentId) && !launcher.Contains(b.persistentId)) continue;
                    // Stack neighbours fit into each other by design (engine mounts, decoupler rims).
                    if (StackNeighbours(a, b)) continue;
                    float depth = Depth(shapes[i].Value, shapes[j].Value);
                    if (depth <= Tolerance) continue;
                    string line = string.Format("{0} and {1} overlap by {2:0.00} m", a.partInfo.title, b.partInfo.title, depth);
                    Debug.Log(string.Format("[LaunchPlanner] check: {0} ({1} / {2})", line, a.partInfo.name, b.partInfo.name));
                    found.Add(line);
                }
            return found;
        }

        static bool StackNeighbours(Part a, Part b)
        {
            Part child = a.parent == b ? a : b.parent == a ? b : null;
            return child != null && child.srfAttachNode != null && child.srfAttachNode.attachedPart != child.parent;
        }

        /// <summary>Deepest penetration between the two parts' colliders (0 if they do not meet).</summary>
        static float Depth(Collider[] a, Collider[] b)
        {
            float deepest = 0;
            foreach (Collider ca in a)
                foreach (Collider cb in b)
                {
                    if (!ca.bounds.Intersects(cb.bounds)) continue;
                    try
                    {
                        Vector3 dir;
                        float d;
                        if (Physics.ComputePenetration(ca, ca.transform.position, ca.transform.rotation,
                            cb, cb.transform.position, cb.transform.rotation, out dir, out d))
                            deepest = Math.Max(deepest, d);
                    }
                    catch (Exception) { }   // concave mesh colliders: not measurable
                }
            return deepest;
        }
    }
}
