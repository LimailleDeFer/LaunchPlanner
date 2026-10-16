using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace LaunchPlanner
{
    /// <summary>Inventaire des pièces chargées par KSP (jeu de base, DLC et mods), triées par type.</summary>
    public static class PartInventory
    {
        static readonly HashSet<string> FuelResources = new HashSet<string>
        {
            "LiquidFuel", "Oxidizer", "SolidFuel", "MonoPropellant", "XenonGas",
            "LqdHydrogen", "LqdMethane", "LqdOxygen", "Kerosene", "Hydrazine",
        };

        static readonly HashSet<string> CommandModules = new HashSet<string> { "ModuleCommand" };

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
                    if (Errors.Count < 20) Errors.Add(ap.name + " : " + e.Message);
                }
            }

            Parts.Sort((a, b) =>
            {
                if (a.Kind != b.Kind) return a.Kind.CompareTo(b.Kind);
                int byThrust = b.ThrustVacKN.CompareTo(a.ThrustVacKN);
                return byThrust != 0 ? byThrust : b.WetMassT.CompareTo(a.WetMassT);
            });

            Debug.Log(string.Format("[LaunchPlanner] {0} pièces chargées, {1} utiles au lanceur, {2} erreurs",
                LoadedCount, Parts.Count, Errors.Count));
            foreach (string err in Errors) Debug.LogWarning("[LaunchPlanner] " + err);
        }

        // En sandbox il n'y a pas d'arbre technologique : tout est disponible.
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
        /// Une pièce donne zéro, une ou plusieurs entrées : un réservoir commutable (B9PartSwitch,
        /// utilisé par CryoTanks et beaucoup de mods) donne une entrée par contenu possible.
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

        /// <summary>Hauteur d'empilement : écart entre le nœud le plus haut et le plus bas.</summary>
        static double StackHeight(Part prefab)
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

        /// <summary>Largeur du modèle 3D (le profil annoncé par certains mods ne correspond pas à la taille réelle).</summary>
        static double ModelWidth(Part prefab)
        {
            try
            {
                Matrix4x4 toPart = prefab.transform.worldToLocalMatrix;
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
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

        /// <summary>Lit la pièce telle que configurée par défaut. Kind vaut Tank pour toute pièce sans moteur ni découpleur.</summary>
        static PartInfo DescribeBase(AvailablePart ap)
        {
            Part prefab = ap.partPrefab;
            PartInfo info = new PartInfo
            {
                Name = ap.name,
                Title = string.IsNullOrEmpty(ap.title) || ap.title.StartsWith("#") ? ap.name : ap.title,   // titre non traduit : on garde le nom interne
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
                // Plusieurs modes (ex. RAPIER) : on garde le plus puissant en vide pour l'instant.
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
                // Propulseur à poudre : poussée non réglable et carburant embarqué.
                info.Kind = solid && best.throttleLocked && info.WetMassT > info.DryMassT ? PartKind.Booster : PartKind.Engine;
                if (info.Kind == PartKind.Booster) info.NoseTop = PointedTop(prefab);
                return info;
            }

            if (HasModule(prefab, DecouplerModules))
            {
                info.Kind = PartKind.Decoupler;
                return info;
            }

            // Les cockpits et capsules embarquent du monergol mais ne sont pas des réservoirs.
            if (HasModule(prefab, CommandModules)) return null;

            info.Kind = PartKind.Tank;
            info.NoseTop = PointedTop(prefab);
            return info;
        }

        // Modèle désactivé (variante non choisie d'une pièce commutable) : le prefab lui-même est inactif, on regarde en dessous.
        static bool Hidden(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (!t.gameObject.activeSelf) return true;
            return false;
        }

        /// <summary>
        /// Le modèle se termine-t-il en pointe ? Vrai si le haut du modèle dépasse nettement le nœud du haut (ou s'il n'y a pas
        /// de nœud du haut), ou si sa tranche supérieure est bien plus étroite que le reste (réservoir à nez, comme ceux de Proton).
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
            // Le prefab reflète la configuration par défaut, y compris celle posée par des mods
            // comme B9PartSwitch. À défaut, on relit les nœuds RESOURCE de la config.
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

        /// <summary>Écrit l'inventaire en CSV (séparateur ;) pour vérification ou diagnostic.</summary>
        public static string ExportCsv(string directory)
        {
            string path = Path.Combine(directory, "inventaire.csv");
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("type;nom;variante;titre;source;tailles;tech;debloquee;cout;masse_seche_t;masse_pleine_t;poussee_vide_kN;poussee_sol_kN;isp_vide_s;isp_sol_s;ergols;ressources;categorie;hauteur_m;diametre_m;nez");
            foreach (PartInfo p in Parts)
            {
                List<string> res = new List<string>();
                foreach (ResourceAmount r in p.Resources) res.Add(r.Name + "=" + r.Amount.ToString("0.##", ci));
                sb.AppendLine(string.Join(";", new string[]
                {
                    p.Kind.ToString(), p.Name, p.Variant, p.Title.Replace(";", ","), p.Source, p.Sizes, p.Tech, p.Unlocked ? "oui" : "non",
                    p.Cost.ToString("0", ci), p.DryMassT.ToString("0.###", ci), p.WetMassT.ToString("0.###", ci),
                    p.ThrustVacKN.ToString("0.#", ci), p.ThrustAslKN.ToString("0.#", ci),
                    p.IspVac.ToString("0", ci), p.IspAsl.ToString("0", ci), p.Propellants, string.Join(" ", res.ToArray()), p.Category,
                    p.HeightM.ToString("0.###", ci), p.DiameterM.ToString("0.###", ci), p.NoseTop ? "oui" : "non",
                }));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }
    }
}
