using System;
using System.Collections.Generic;
using System.Linq;
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
        const float Tolerance = 0.08f;           // m: parts that merely touch, or a collider a little larger than its model
        const float AttachedTolerance = 0.3f;    // m: a part and the one it is attached to

        public static List<string> Overlaps(List<Part> parts, HashSet<uint> launcher)
        {
            List<string> found = new List<string>();
            // Colliders of parts just loaded in the editor may still carry their old place until physics catches up.
            Physics.SyncTransforms();
            Dictionary<Part, Bounds> models = new Dictionary<Part, Bounds>();
            List<KeyValuePair<Part, Collider[]>> shapes = new List<KeyValuePair<Part, Collider[]>>();
            foreach (Part p in parts)
            {
                // Fuel lines and struts run from one part into another by design.
                if (p == null || p is CompoundPart) continue;
                List<Collider> usable = new List<Collider>();
                Bounds model = ModelBounds(p);
                models[p] = model;
                foreach (Collider c in p.GetComponentsInChildren<Collider>(false))
                {
                    if (c == null || !c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy) continue;
                    // A concave mesh gives meaningless penetration depths.
                    MeshCollider mesh = c as MeshCollider;
                    if (mesh != null && !mesh.convex) continue;
                    // A collider far larger than the part's model (helper volumes of some mods) is not its hull.
                    if (model.size.magnitude > 0.01f && c.bounds.size.magnitude > 2 * model.size.magnitude + 0.5f) continue;
                    usable.Add(c);
                }
                if (usable.Count > 0) shapes.Add(new KeyValuePair<Part, Collider[]>(p, usable.ToArray()));
            }
            int pairs = 0;
            Dictionary<Part, List<string>> byPart = new Dictionary<Part, List<string>>();
            for (int i = 0; i < shapes.Count; i++)
                for (int j = i + 1; j < shapes.Count; j++)
                {
                    Part a = shapes[i].Key, b = shapes[j].Key;
                    if (launcher.Count > 0 && !launcher.Contains(a.persistentId) && !launcher.Contains(b.persistentId)) continue;
                    // Stack neighbours fit into each other by design (engine mounts, decoupler rims).
                    if (StackNeighbours(a, b)) continue;
                    // Parts whose visible models are apart cannot sink into each other.
                    Bounds ma = models[a], mb = models[b];
                    if (ma.size.sqrMagnitude > 0 && mb.size.sqrMagnitude > 0 && !ma.Intersects(mb)) continue;
                    pairs++;
                    float depth = Depth(shapes[i].Value, shapes[j].Value);
                    // A part on the surface of its parent sits a little into it (radial decouplers, thrusters).
                    if (depth <= (a.parent == b || b.parent == a ? AttachedTolerance : Tolerance)) continue;
                    string line = string.Format("{0} and {1} overlap by {2:0.00} m", a.partInfo.title, b.partInfo.title, depth);
                    Debug.Log(string.Format("[LaunchPlanner] check: {0} ({1} / {2})", line, a.partInfo.name, b.partInfo.name));
                    found.Add(line);
                    foreach (Part q in new[] { a, b })
                    {
                        if (!byPart.ContainsKey(q)) byPart[q] = new List<string>();
                        byPart[q].Add(line);
                    }
                }
            // When most neighbouring pairs seem to overlap, the measure itself is wrong: say so instead of listing them.
            if (found.Count > 10 && found.Count > pairs / 3)
            {
                Debug.Log(string.Format("[LaunchPlanner] check: {0} of {1} pairs reported, measure unreliable", found.Count, pairs));
                return new List<string> { string.Format("check not reliable here ({0} of {1} neighbouring pairs reported), ignored", found.Count, pairs) };
            }
            // A part said to sink into many others has a collider the check cannot read: one line for it instead.
            foreach (KeyValuePair<Part, List<string>> e in byPart)
                if (e.Value.Count > 4)
                {
                    found.RemoveAll(l => e.Value.Contains(l));
                    found.Add(string.Format("{0}: collider not measurable ({1} overlaps ignored)", e.Key.partInfo.title, e.Value.Count));
                }
            return found;
        }

        static bool StackNeighbours(Part a, Part b)
        {
            Part child = a.parent == b ? a : b.parent == a ? b : null;
            return child != null && child.srfAttachNode != null && child.srfAttachNode.attachedPart != child.parent;
        }

        static Bounds ModelBounds(Part p)
        {
            Bounds b = new Bounds(p.transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in p.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            return b;
        }

        /// <summary>Deepest penetration between the two parts' colliders (0 if they do not meet).</summary>
        static float Depth(Collider[] a, Collider[] b)
        {
            float deepest = 0;
            foreach (Collider ca in a)
                foreach (Collider cb in b)
                {
                    if (!ca.bounds.Intersects(cb.bounds)) continue;
                    // No real penetration can be deeper than the overlap of the two boxes.
                    Vector3 lo = Vector3.Max(ca.bounds.min, cb.bounds.min), hi = Vector3.Min(ca.bounds.max, cb.bounds.max);
                    float box = Mathf.Min(hi.x - lo.x, Mathf.Min(hi.y - lo.y, hi.z - lo.z));
                    try
                    {
                        Vector3 dir;
                        float d;
                        if (Physics.ComputePenetration(ca, ca.transform.position, ca.transform.rotation,
                            cb, cb.transform.position, cb.transform.rotation, out dir, out d))
                            deepest = Math.Max(deepest, Math.Min(d, box));
                    }
                    catch (Exception) { }   // concave mesh colliders: not measurable
                }
            return deepest;
        }
    }
}
