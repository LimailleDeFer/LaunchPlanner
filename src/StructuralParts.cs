using System;
using System.Collections.Generic;
using UnityEngine;

namespace LaunchPlanner
{
    /// <summary>Fuel-less parts useful to the builder: booster nose cones, multi-couplers for clusters.</summary>
    public static class StructuralParts
    {
        static readonly HashSet<string> Excluded = new HashSet<string>
        {
            "ModuleEngines", "ModuleEnginesFX", "ModuleDecouple", "ModuleAnchoredDecoupler", "ModuleCommand",
            "ModuleParachute", "ModuleProceduralFairing", "ModuleDockingNode", "ModuleCargoBay", "ModuleWheelBase",
        };

        static bool Usable(AvailablePart ap, out Part prefab)
        {
            prefab = ap != null ? ap.partPrefab : null;
            if (prefab == null) return false;
            if (PartInventory.SafeHasResearch() && !PartInventory.SafePurchased(ap)) return false;
            if (prefab.Resources != null && prefab.Resources.Count > 0) return false;
            foreach (PartModule m in prefab.Modules)
                if (m != null && Excluded.Contains(m.moduleName)) return false;
            return true;
        }

        static List<AttachNode> StackNodes(Part prefab)
        {
            List<AttachNode> nodes = new List<AttachNode>();
            foreach (AttachNode n in prefab.attachNodes)
                if (n != null && n.nodeType == AttachNode.NodeType.Stack) nodes.Add(n);
            return nodes;
        }

        static PartInfo Info(AvailablePart ap, Part prefab)
        {
            return new PartInfo
            {
                Name = ap.name,
                Title = string.IsNullOrEmpty(ap.title) || ap.title.StartsWith("#") ? ap.name : ap.title,
                Sizes = Refl.Str(ap, "bulkheadProfiles"),
                Category = ap.category.ToString(),
                Kind = PartKind.Tank,
                Unlocked = true,
                DryMassT = prefab.mass,
                WetMassT = prefab.mass,
                Cost = ap.cost,
            };
        }

        /// <summary>Diameter of a node from its size (0 = 0.625 m, 1 = 1.25 m, 2 = 2.5 m...).</summary>
        static double NodeDiameter(int size)
        {
            return size <= 0 ? 0.625 : 1.25 * size;
        }

        /// <summary>Nose cones: a single stack node, facing down, with the model above it.</summary>
        public static List<PartInfo> NoseCones()
        {
            List<PartInfo> cones = new List<PartInfo>();
            if (PartLoader.LoadedPartsList == null) return cones;
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                try
                {
                    Part prefab;
                    if (!Usable(ap, out prefab)) continue;
                    if (ap.category != PartCategories.Aero && ap.category != PartCategories.Structural) continue;
                    List<AttachNode> nodes = StackNodes(prefab);
                    if (nodes.Count != 1 || nodes[0].orientation.y > -0.5f) continue;
                    if (!PartInventory.PointedTop(prefab)) continue;
                    cones.Add(Info(ap, prefab));
                }
                catch (Exception) { }
            }
            return cones;
        }

        /// <summary>Adapters: two stack nodes, the narrow one on top, two profiles (e.g. Rockomax adapter 2.5 m → 1.25 m).</summary>
        public static List<PartInfo> Adapters()
        {
            List<PartInfo> adapters = new List<PartInfo>();
            if (PartLoader.LoadedPartsList == null) return adapters;
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                try
                {
                    Part prefab;
                    if (!Usable(ap, out prefab)) continue;
                    List<AttachNode> nodes = StackNodes(prefab);
                    if (nodes.Count != 2) continue;
                    AttachNode top = nodes[0].position.y > nodes[1].position.y ? nodes[0] : nodes[1];
                    AttachNode bottom = top == nodes[0] ? nodes[1] : nodes[0];
                    if (top.orientation.y < 0.5f || bottom.orientation.y > -0.5f || top.size >= bottom.size) continue;
                    PartInfo info = Info(ap, prefab);
                    List<string> profiles = new List<string>();
                    foreach (string raw in (info.Sizes ?? "").Split(new[] { ',' }))
                    {
                        string s = raw.Trim();
                        if (s.StartsWith("size") && !profiles.Contains(s)) profiles.Add(s);
                    }
                    if (profiles.Count != 2) continue;
                    profiles.Sort((a, b) => CraftBuilder.DiameterOf(a).CompareTo(CraftBuilder.DiameterOf(b)));
                    if (CraftBuilder.DiameterOf(profiles[0]) >= CraftBuilder.DiameterOf(profiles[1])) continue;
                    info.AdapterTop = profiles[0];
                    info.AdapterBottom = profiles[1];
                    info.HeightM = top.position.y - bottom.position.y;
                    adapters.Add(info);
                }
                catch (Exception) { }
            }
            return adapters;
        }

        /// <summary>Multi-couplers: one node on top, at least two nodes on the bottom, side by side.</summary>
        public static List<PartInfo> MultiCouplers()
        {
            List<PartInfo> couplers = new List<PartInfo>();
            if (PartLoader.LoadedPartsList == null) return couplers;
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                try
                {
                    Part prefab;
                    if (!Usable(ap, out prefab)) continue;
                    List<AttachNode> nodes = StackNodes(prefab);
                    if (nodes.Count < 3) continue;
                    AttachNode top = null;
                    float bottomY = float.MaxValue;
                    foreach (AttachNode n in nodes)
                    {
                        if (top == null || n.position.y > top.position.y) top = n;
                        bottomY = Math.Min(bottomY, n.position.y);
                    }
                    List<AttachNode> bottoms = nodes.FindAll(n => Math.Abs(n.position.y - bottomY) < 0.05f && n.orientation.y < -0.5f);
                    if (bottoms.Count < 2 || bottoms.Count + 1 != nodes.Count || top.position.y - bottomY < 0.05f) continue;
                    double spacing = double.MaxValue;
                    for (int i = 0; i < bottoms.Count; i++)
                        for (int j = i + 1; j < bottoms.Count; j++)
                            spacing = Math.Min(spacing, (bottoms[i].position - bottoms[j].position).magnitude);
                    if (spacing < 0.3) continue;
                    PartInfo info = Info(ap, prefab);
                    info.StackOutputs = bottoms.Count;
                    info.TopDiameterM = NodeDiameter(top.size);
                    info.OutputDiameterM = NodeDiameter(bottoms[0].size);
                    info.OutputSpacingM = spacing;
                    info.HeightM = top.position.y - bottomY;
                    couplers.Add(info);
                }
                catch (Exception) { }
            }
            return couplers;
        }

        /// <summary>Profile of the lowest free node of the payload (where the launcher attaches), or "".</summary>
        public static string PayloadBottomSize()
        {
            try
            {
                EditorLogic editor = EditorLogic.fetch;
                ShipConstruct ship = editor != null ? editor.ship : null;
                if (ship == null || ship.parts == null) return "";
                AttachNode best = null;
                float bestY = float.MaxValue;
                foreach (Part p in ship.parts)
                    foreach (AttachNode n in p.attachNodes)
                    {
                        if (n == null || n.nodeType != AttachNode.NodeType.Stack || n.attachedPart != null) continue;
                        if (p.transform.TransformDirection(n.orientation).y > -0.5f) continue;
                        float y = p.transform.TransformPoint(n.position).y;
                        if (y < bestY) { bestY = y; best = n; }
                    }
                return best == null ? "" : "size" + Math.Max(0, best.size);
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
