using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace LaunchPlanner
{
    /// <summary>Inventory of the parts loaded by KSP (stock, DLC and mods), sorted by kind.</summary>
    public static class PartInventory
    {
        static readonly HashSet<string> FuelResources = new HashSet<string>
        {
            "LiquidFuel", "Oxidizer", "SolidFuel", "MonoPropellant", "XenonGas",
            "LqdHydrogen", "LqdMethane", "LqdOxygen", "Kerosene", "Hydrazine",
        };

        static readonly HashSet<string> CommandModules = new HashSet<string> { "ModuleCommand" };

        static readonly HashSet<string> StorageModules = new HashSet<string>
        {
            "ModuleCargoBay", "ModuleInventoryPart",
        };

        static readonly HashSet<string> DecouplerModules = new HashSet<string>
        {
            "ModuleDecouple", "ModuleAnchoredDecoupler",
        };

        public static List<PartInfo> Parts = new List<PartInfo>();
        public static int LoadedCount;
        public static List<string> Errors = new List<string>();

        public static void Refresh()
        {
            Parts.Clear();
            Errors.Clear();
            LoadedCount = 0;

            bool hasRnD = SafeHasResearch();
            foreach (object o in Refl.Items(Refl.GetStatic(typeof(PartLoader), "LoadedPartsList")))
            {
                AvailablePart ap = o as AvailablePart;
                if (ap == null || ap.partPrefab == null) continue;
                LoadedCount++;
                try
                {
                    bool unlocked = !hasRnD || SafePurchased(ap);
                    foreach (PartInfo info in Describe(ap))
                    {
                        info.Unlocked = unlocked;
                        Parts.Add(info);
                    }
                }
                catch (Exception e)
                {
                    if (Errors.Count < 20) Errors.Add(ap.name + ": " + e.Message);
                }
            }

            Parts.Sort((a, b) =>
            {
                if (a.Kind != b.Kind) return a.Kind.CompareTo(b.Kind);
                int byThrust = b.ThrustVacKN.CompareTo(a.ThrustVacKN);
                return byThrust != 0 ? byThrust : b.WetMassT.CompareTo(a.WetMassT);
            });

            Debug.Log(string.Format("[LaunchPlanner] {0} parts loaded, {1} useful to the launcher, {2} errors",
                LoadedCount, Parts.Count, Errors.Count));
            foreach (string err in Errors) Debug.LogWarning("[LaunchPlanner] " + err);
        }

        // In sandbox there is no tech tree: everything is available.
        public static bool SafeHasResearch()
        {
            return Refl.GetStatic(typeof(ResearchAndDevelopment), "Instance") != null;
        }

        public static bool SafePurchased(AvailablePart ap)
        {
            try { return ResearchAndDevelopment.PartModelPurchased(ap); }
            catch { return true; }
        }

        /// <summary>
        /// A part yields zero, one or several entries: a switchable tank (B9PartSwitch,
        /// used by CryoTanks and many mods) yields one entry per possible content.
        /// </summary>
        static List<PartInfo> Describe(AvailablePart ap)
        {
            List<PartInfo> result = new List<PartInfo>();
            PartInfo info = DescribeBase(ap);
            if (info == null) return result;
            if (info.Kind != PartKind.Tank)
            {
                result.Add(info);
                return result;
            }
            result.AddRange(B9TankVariants(ap.partPrefab, info));
            if (result.Count == 0 && HasFuel(info)) result.Add(info);
            return result;
        }

        static bool HasFuel(PartInfo info)
        {
            foreach (ResourceAmount r in info.Resources)
                if (FuelResources.Contains(r.Name) && r.Amount > 0) return true;
            return false;
        }

        static List<PartInfo> B9TankVariants(Part prefab, PartInfo baseInfo)
        {
            List<PartInfo> variants = new List<PartInfo>();
            foreach (object module in Refl.Items(Refl.Get(prefab, "Modules")))
            {
                if (module == null || module.GetType().Name != "ModuleB9PartSwitch") continue;
                foreach (object subtype in Refl.Items(Refl.Get(module, "subtypes")))
                {
                    object tankType = Refl.Get(subtype, "tankType");
                    if (tankType == null) continue;
                    double volume = ToDouble(Refl.Call(module, "GetTotalVolume", subtype));
                    if (volume <= 0) continue;

                    string name = Refl.Str(subtype, "subtypeName");
                    string title = Refl.Str(subtype, "title");
                    PartInfo v = new PartInfo
                    {
                        Name = baseInfo.Name,
                        Variant = name,
                        VariantModule = Refl.Str(module, "moduleID"),
                        Title = baseInfo.Title + " [" + (title.Length > 0 ? title : name) + "]",
                        Source = baseInfo.Source,
                        Sizes = baseInfo.Sizes,
                        Tech = baseInfo.Tech,
                        Category = baseInfo.Category,
                        HeightM = baseInfo.HeightM,
                        DiameterM = baseInfo.DiameterM,
                        NoseTop = baseInfo.NoseTop,
                        Storage = baseInfo.Storage,
                        Cost = baseInfo.Cost,
                        Kind = PartKind.Tank,
                        DryMassT = baseInfo.DryMassT + ToDouble(Refl.Call(module, "GetDryMass", subtype)),
                    };
                    foreach (object res in Refl.Items(Refl.Get(tankType, "resources")))
                    {
                        string resName = Refl.Str(res, "ResourceName");
                        if (resName.Length > 0) Add(v, resName, Refl.Num(res, "unitsPerVolume") * volume);
                    }
                    if (!HasFuel(v)) continue;
                    v.WetMassT = v.DryMassT;
                    foreach (ResourceAmount r in v.Resources) v.WetMassT += r.MassT;
                    variants.Add(v);
                }
            }
            return variants;
        }

        /// <summary>Stack height: gap between the highest and lowest node.</summary>
        internal static double StackHeight(Part prefab)
        {
            double top = double.MinValue, bottom = double.MaxValue;
            int n = 0;
            foreach (AttachNode node in prefab.attachNodes)
            {
                if (node == null || node.nodeType != AttachNode.NodeType.Stack) continue;
                top = Math.Max(top, node.position.y);
                bottom = Math.Min(bottom, node.position.y);
                n++;
            }
            return n >= 2 ? top - bottom : 0;
        }

        /// <summary>Width of the 3D model (the profile declared by some mods does not match the real size).</summary>
        internal static double ModelWidth(Part prefab)
        {
            try
            {
                Matrix4x4 toPart = prefab.transform.worldToLocalMatrix;
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                // Shrouds and fairings that drop off (ModuleJettison) are not the part's own width.
                HashSet<string> jettison = new HashSet<string>();
                foreach (ModuleJettison j in prefab.FindModulesImplementing<ModuleJettison>())
                    if (!string.IsNullOrEmpty(j.jettisonName))
                        foreach (string n in j.jettisonName.Split(',')) jettison.Add(n.Trim());
                foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    // Meshes switched off on the prefab (variants, hidden options) and jettisonable shrouds are skipped.
                    bool hidden = false;
                    for (Transform t = mf.transform; t != null && t != prefab.transform; t = t.parent)
                        if (!t.gameObject.activeSelf || jettison.Contains(t.name)) { hidden = true; break; }
                    MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                    if (hidden || (mr != null && !mr.enabled)) continue;
                    Bounds b = mf.sharedMesh.bounds;
                    Matrix4x4 m = toPart * mf.transform.localToWorldMatrix;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector3 v = m.MultiplyPoint3x4(c);
                        minX = Math.Min(minX, v.x); maxX = Math.Max(maxX, v.x);
                        minZ = Math.Min(minZ, v.z); maxZ = Math.Max(maxZ, v.z);
                    }
                }
                return maxX > minX ? Math.Min(maxX - minX, maxZ - minZ) : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        static double ToDouble(object value)
        {
            if (value == null) return 0;
            try { return Convert.ToDouble(value); } catch { return 0; }
        }

        /// <summary>Reads the part as configured by default. Kind is Tank for any part without an engine or decoupler.</summary>
        static PartInfo DescribeBase(AvailablePart ap)
        {
            Part prefab = ap.partPrefab;
            PartInfo info = new PartInfo
            {
                Name = ap.name,
                Title = string.IsNullOrEmpty(ap.title) || ap.title.StartsWith("#") ? ap.name : ap.title,   // untranslated title: keep the internal name
                Source = SourceOf(Refl.Str(ap, "partUrl")),
                Sizes = Refl.Str(ap, "bulkheadProfiles"),
                Tech = Refl.Str(ap, "TechRequired"),
                Category = Refl.Get(ap, "category") != null ? Refl.Get(ap, "category").ToString() : "",
                Cost = Refl.Num(ap, "cost"),
                DryMassT = Refl.Num(prefab, "mass"),
                HeightM = StackHeight(prefab),
                DiameterM = ModelWidth(prefab),
            };

            ReadResources(ap, prefab, info);
            info.WetMassT = info.DryMassT;
            foreach (ResourceAmount r in info.Resources) info.WetMassT += r.MassT;

            List<ModuleEngines> engines = prefab.FindModulesImplementing<ModuleEngines>();
            if (engines != null && engines.Count > 0)
            {
                // Several modes (e.g. RAPIER): for now we keep the most powerful one in vacuum.
                ModuleEngines best = engines[0];
                foreach (ModuleEngines e in engines) if (e.maxThrust > best.maxThrust) best = e;
                if (best.maxThrust <= 0 || best.atmosphereCurve == null) return null;

                info.ThrustVacKN = best.maxThrust;
                info.IspVac = best.atmosphereCurve.Evaluate(0f);
                info.IspAsl = best.atmosphereCurve.Evaluate(1f);
                info.ThrustAslKN = info.IspVac > 0 ? best.maxThrust * info.IspAsl / info.IspVac : 0;
                info.ThrottleLocked = best.throttleLocked;

                bool solid = false;
                List<string> names = new List<string>();
                if (best.propellants != null)
                    foreach (Propellant p in best.propellants)
                    {
                        names.Add(p.name);
                        if (p.name == "SolidFuel") solid = true;
                    }
                info.Propellants = string.Join(" + ", names.ToArray());
                // Solid motor: non-adjustable thrust and onboard fuel.
                info.Kind = solid && best.throttleLocked && info.WetMassT > info.DryMassT ? PartKind.Booster : PartKind.Engine;
                if (info.Kind == PartKind.Booster) info.NoseTop = PointedTop(prefab);
                return info;
            }

            if (HasModule(prefab, DecouplerModules))
            {
                info.Kind = PartKind.Decoupler;
                return info;
            }

            // Cockpits and capsules carry monopropellant but are not tanks.
            if (HasModule(prefab, CommandModules)) return null;

            info.Kind = PartKind.Tank;
            info.NoseTop = PointedTop(prefab);
            info.Storage = HasModule(prefab, StorageModules);
            return info;
        }

        /// <summary>
        /// The part can go on a surface. KSP gives every part a surface node, even one that only stacks (Glenn legs,
        /// pod parachutes): the attach rules say whether the editor allows it.
        /// </summary>
        internal static bool SurfaceMountable(Part prefab)
        {
            return prefab.srfAttachNode != null && prefab.attachRules != null && prefab.attachRules.srfAttach;
        }

        // Disabled model (unchosen variant of a switchable part): the prefab itself is inactive, so we look below it.
        internal static bool Hidden(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (!t.gameObject.activeSelf) return true;
            return false;
        }

        /// <summary>
        /// Does the model end in a point? True if the top of the model clearly extends past the top node (or if there is no
        /// top node), or if its top slice is much narrower than the rest (nose tank, like Proton's).
        /// </summary>
        public static bool PointedTop(Part prefab)
        {
            try
            {
                Matrix4x4 toPart = prefab.transform.worldToLocalMatrix;
                List<Vector3> points = new List<Vector3>();
                foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || !mf.sharedMesh.isReadable || Hidden(mf.transform, prefab.transform)) continue;
                    Matrix4x4 m = toPart * mf.transform.localToWorldMatrix;
                    foreach (Vector3 v in mf.sharedMesh.vertices) points.Add(m.MultiplyPoint3x4(v));
                }
                if (points.Count < 8) return false;
                float minY = float.MaxValue, maxY = float.MinValue, maxR = 0;
                foreach (Vector3 v in points)
                {
                    minY = Math.Min(minY, v.y);
                    maxY = Math.Max(maxY, v.y);
                    maxR = Math.Max(maxR, new Vector2(v.x, v.z).magnitude);
                }
                if (maxR <= 0 || maxY <= minY) return false;

                double topNode = double.MinValue;
                int stack = 0;
                foreach (AttachNode n in prefab.attachNodes)
                    if (n != null && n.nodeType == AttachNode.NodeType.Stack) { stack++; topNode = Math.Max(topNode, n.position.y); }
                if (stack >= 2 && maxY > topNode + 0.6 * maxR) return true;

                float band = maxY - 0.1f * (maxY - minY), topR = 0;
                foreach (Vector3 v in points)
                    if (v.y >= band) topR = Math.Max(topR, new Vector2(v.x, v.z).magnitude);
                return topR < 0.55f * maxR;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool HasModule(object prefab, HashSet<string> classNames)
        {
            foreach (object m in Refl.Items(Refl.Get(prefab, "Modules")))
                if (m != null && classNames.Contains(m.GetType().Name)) return true;
            return false;
        }

        static void ReadResources(AvailablePart ap, Part prefab, PartInfo info)
        {
            // The prefab reflects the default configuration, including the one set by mods
            // such as B9PartSwitch. Failing that, the config's RESOURCE nodes are read again.
            foreach (object r in Refl.Items(Refl.Get(prefab, "Resources")))
            {
                string name = Refl.Str(r, "resourceName");
                double amount = Refl.Num(r, "amount");
                if (name.Length > 0) Add(info, name, amount);
            }
            if (info.Resources.Count > 0) return;

            ConfigNode cfg = Refl.Get(ap, "partConfig") as ConfigNode;
            ConfigNode[] nodes = cfg == null ? null : cfg.GetNodes("RESOURCE");
            if (nodes == null) return;
            foreach (ConfigNode n in nodes)
            {
                double amount;
                double.TryParse(n.GetValue("amount"), NumberStyles.Float, CultureInfo.InvariantCulture, out amount);
                string name = n.GetValue("name");
                if (!string.IsNullOrEmpty(name)) Add(info, name, amount);
            }
        }

        static void Add(PartInfo info, string name, double amount)
        {
            info.Resources.Add(new ResourceAmount { Name = name, Amount = amount, MassT = amount * Density(name) });
        }

        static readonly Dictionary<string, double> densityCache = new Dictionary<string, double>();

        static double Density(string resource)
        {
            double d;
            if (densityCache.TryGetValue(resource, out d)) return d;
            object lib = Refl.GetStatic(typeof(PartResourceLibrary), "Instance");
            object def = Refl.Call(lib, "GetDefinition", resource);
            d = Refl.Num(def, "density");
            densityCache[resource] = d;
            return d;
        }

        static string SourceOf(string partUrl)
        {
            if (string.IsNullOrEmpty(partUrl)) return "?";
            int slash = partUrl.IndexOf('/');
            string root = slash < 0 ? partUrl : partUrl.Substring(0, slash);
            if (root == "Squad") return "KSP";
            if (root == "SquadExpansion")
            {
                string rest = partUrl.Substring(slash + 1);
                if (rest.StartsWith("MakingHistory")) return "Making History";
                if (rest.StartsWith("Serenity")) return "Breaking Ground";
                return "DLC";
            }
            return root;
        }

        /// <summary>Writes the inventory as CSV (; separator) for verification or diagnostics.</summary>
        public static string ExportCsv(string directory)
        {
            string path = Path.Combine(directory, "inventory.csv");
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("type;name;variant;title;source;sizes;tech;unlocked;cost;dry_mass_t;wet_mass_t;thrust_vac_kN;thrust_asl_kN;isp_vac_s;isp_asl_s;propellants;resources;category;height_m;diameter_m;nose;storage");
            foreach (PartInfo p in Parts)
            {
                List<string> res = new List<string>();
                foreach (ResourceAmount r in p.Resources) res.Add(r.Name + "=" + r.Amount.ToString("0.##", ci));
                sb.AppendLine(string.Join(";", new string[]
                {
                    p.Kind.ToString(), p.Name, p.Variant, p.Title.Replace(";", ","), p.Source, p.Sizes, p.Tech, p.Unlocked ? "yes" : "no",
                    p.Cost.ToString("0", ci), p.DryMassT.ToString("0.###", ci), p.WetMassT.ToString("0.###", ci),
                    p.ThrustVacKN.ToString("0.#", ci), p.ThrustAslKN.ToString("0.#", ci),
                    p.IspVac.ToString("0", ci), p.IspAsl.ToString("0", ci), p.Propellants, string.Join(" ", res.ToArray()), p.Category,
                    p.HeightM.ToString("0.###", ci), p.DiameterM.ToString("0.###", ci), p.NoseTop ? "yes" : "no",
                    p.Storage ? "yes" : "no",
                }));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }
    }
}
