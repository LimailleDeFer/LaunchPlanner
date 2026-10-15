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
        static bool SafeHasResearch()
        {
            return Refl.GetStatic(typeof(ResearchAndDevelopment), "Instance") != null;
        }

        static bool SafePurchased(AvailablePart ap)
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
                        Title = baseInfo.Title + " [" + (title.Length > 0 ? title : name) + "]",
                        Source = baseInfo.Source,
                        Sizes = baseInfo.Sizes,
                        Tech = baseInfo.Tech,
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
                Cost = Refl.Num(ap, "cost"),
                DryMassT = Refl.Num(prefab, "mass"),
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
            return info;
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
            sb.AppendLine("type;nom;variante;titre;source;tailles;tech;debloquee;cout;masse_seche_t;masse_pleine_t;poussee_vide_kN;poussee_sol_kN;isp_vide_s;isp_sol_s;ergols;ressources");
            foreach (PartInfo p in Parts)
            {
                List<string> res = new List<string>();
                foreach (ResourceAmount r in p.Resources) res.Add(r.Name + "=" + r.Amount.ToString("0.##", ci));
                sb.AppendLine(string.Join(";", new string[]
                {
                    p.Kind.ToString(), p.Name, p.Variant, p.Title.Replace(";", ","), p.Source, p.Sizes, p.Tech, p.Unlocked ? "oui" : "non",
                    p.Cost.ToString("0", ci), p.DryMassT.ToString("0.###", ci), p.WetMassT.ToString("0.###", ci),
                    p.ThrustVacKN.ToString("0.#", ci), p.ThrustAslKN.ToString("0.#", ci),
                    p.IspVac.ToString("0", ci), p.IspAsl.ToString("0", ci), p.Propellants, string.Join(" ", res.ToArray()),
                }));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }
    }
}
