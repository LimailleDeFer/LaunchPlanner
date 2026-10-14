using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace LaunchPlanner
{
    public enum PartKind { Engine, Booster, Tank, Decoupler }

    public class ResourceAmount
    {
        public string Name;
        public double Amount;
        public double MassT;
    }

    /// <summary>Ce que le planificateur sait d'une pièce : tout ce qui sert au calcul du Δv.</summary>
    public class PartInfo
    {
        public string Name;
        public string Title;
        public string Source;      // dossier de GameData d'où vient la pièce (Squad, SquadExpansion, nom du mod…)
        public string Sizes;       // profils de fixation, ex. "size1, srf"
        public string Tech;
        public bool Unlocked;
        public PartKind Kind;
        public double Cost;
        public double DryMassT;
        public double WetMassT;
        public List<ResourceAmount> Resources = new List<ResourceAmount>();

        // Moteurs et boosters
        public double ThrustVacKN;
        public double ThrustAslKN;
        public double IspVac;
        public double IspAsl;
        public bool ThrottleLocked;
        public string Propellants = "";

        public const double G0 = 9.80665;

        /// <summary>Durée de combustion à pleine poussée avec le carburant embarqué (boosters).</summary>
        public double BurnTimeS
        {
            get
            {
                if (IspVac <= 0 || ThrustVacKN <= 0) return 0;
                double massFlowT = ThrustVacKN / (IspVac * G0); // t/s
                return (WetMassT - DryMassT) / massFlowT;
            }
        }
    }

    /// <summary>Inventaire des pièces chargées par KSP (jeu de base, DLC et mods), triées par type.</summary>
    public static class PartInventory
    {
        static readonly HashSet<string> FuelResources = new HashSet<string>
        {
            "LiquidFuel", "Oxidizer", "SolidFuel", "MonoPropellant", "XenonGas",
            "LqdHydrogen", "LqdMethane", "LqdOxygen", "Kerosene", "Hydrazine",
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
                    PartInfo info = Describe(ap);
                    if (info == null) continue;
                    info.Unlocked = !hasRnD || SafePurchased(ap);
                    Parts.Add(info);
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

        static PartInfo Describe(AvailablePart ap)
        {
            Part prefab = ap.partPrefab;
            PartInfo info = new PartInfo
            {
                Name = ap.name,
                Title = string.IsNullOrEmpty(ap.title) ? ap.name : ap.title,
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

            foreach (ResourceAmount r in info.Resources)
                if (FuelResources.Contains(r.Name) && r.Amount > 0)
                {
                    info.Kind = PartKind.Tank;
                    return info;
                }

            return null;
        }

        static bool HasModule(Part prefab, HashSet<string> classNames)
        {
            List<PartModule> modules = prefab.FindModulesImplementing<PartModule>();
            if (modules == null) return false;
            foreach (PartModule m in modules)
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
            sb.AppendLine("type;nom;titre;source;tailles;tech;debloquee;cout;masse_seche_t;masse_pleine_t;poussee_vide_kN;poussee_sol_kN;isp_vide_s;isp_sol_s;ergols;ressources");
            foreach (PartInfo p in Parts)
            {
                List<string> res = new List<string>();
                foreach (ResourceAmount r in p.Resources) res.Add(r.Name + "=" + r.Amount.ToString("0.##", ci));
                sb.AppendLine(string.Join(";", new string[]
                {
                    p.Kind.ToString(), p.Name, p.Title.Replace(";", ","), p.Source, p.Sizes, p.Tech, p.Unlocked ? "oui" : "non",
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
