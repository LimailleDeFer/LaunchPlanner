using System;
using System.Collections.Generic;

namespace LaunchPlanner
{
    public class TankUse
    {
        public PartInfo Tank;
        public int Count;
    }

    /// <summary>Boosters montés autour du premier étage.</summary>
    public class BoosterPlan
    {
        public int Count;
        public bool Solid;
        public bool Crossfeed;          // asperges : les boosters alimentent le corps central et sont largués 2 par 2
        public PartInfo Part;           // propulseur à poudre, ou moteur d'un booster liquide
        public string Mixture = "";
        public List<TankUse> Tanks = new List<TankUse>();
        public double Fuel;             // ergols par booster (t)
        public double TankDry;          // masse à vide des réservoirs d'un booster (t)
        public PartInfo Decoupler;      // découpleur radial
        public double DecouplerMass;
        public PartInfo Separator;      // fusée de séparation (Sepratron) qui écarte le booster largué
        public int SeparatorsEach;      // nombre par booster, selon sa masse à vide

        public double DryEach
        {
            get { return Part.DryMassT + (Solid ? 0 : TankDry) + DecouplerMass; }
        }

        public double FuelEach
        {
            get { return Solid ? Part.WetMassT - Part.DryMassT : Fuel; }
        }
    }

    public class StagePlan
    {
        public PartInfo Engine;
        public int EngineCount;
        public string Mixture = "";
        public string Size = "";
        public List<TankUse> Tanks = new List<TankUse>();
        public double Fuel;             // t
        public double TankDry;          // t
        public PartInfo Decoupler;      // découpleur sous l'étage (aucun pour le premier étage)
        public double DecouplerMass;
        public BoosterPlan Boosters;

        // Résultats de la simulation
        public double DeltaV;
        public double StartDeltaV;      // Δv déjà fourni par les étages inférieurs à l'allumage
        public double CoreTwr;          // TWR du corps central seul, une fois les boosters largués
        public double CoreStartDeltaV;  // Δv cumulé au largage des boosters
        public double Cost;
        public double Twr;
        public double MassStart;

        public double DryMass
        {
            get { return EngineCount * Engine.DryMassT + TankDry + DecouplerMass; }
        }

        public double TotalMass
        {
            get
            {
                double m = DryMass + Fuel;
                if (Boosters != null) m += Boosters.Count * (Boosters.DryEach + Boosters.FuelEach);
                return m;
            }
        }
    }

    public class LauncherDesign
    {
        public string Variant;
        public string Description;
        public List<StagePlan> Stages = new List<StagePlan>();   // du premier étage (bas) au dernier (haut)
        public bool Valid;
        public string Problem = "";
        public double TotalMass;      // lanceur + charge utile
        public double LauncherMass;
        public double Cost;
        public double DeltaV;
        public double LiftoffTwr;
        public List<string> Notes = new List<string>();
        public PartInfo PayloadDecoupler;   // découpleur entre la charge utile et le dernier étage

        // Étage atterrisseur, sous la charge utile, quand elle ne peut pas se poser seule
        public StagePlan Lander;
        public PartInfo LanderLegs;
        public int LanderLegCount;

        // Encombrement estimé, charge utile comprise
        public double HeightM;
        public double WidthM;
        public int PartCount;
    }

    public class DesignRequest
    {
        public List<PartInfo> Parts;
        public double PayloadMass;      // t
        public double TargetDeltaV;     // m/s que le lanceur doit fournir
        public double HomeGravity = 9.81;
        public bool HomeAtmosphere = true;
        public double MinLiftoffTwr = 1.3;
        public double MinUpperTwr = 0.5;
        public double AscentDeltaV = 3400; // Δv de la mise en orbite depuis le sol : les étages allumés avant ont besoin de poussée
        public bool MinimizeCost;

        // Limites du VAB et du pas de tir (0 = pas de limite, par exemple en mode bac à sable)
        public double MaxHeight;        // m
        public double MaxWidth;         // m
        public int MaxParts;
        public double MaxMass;          // t, au décollage
        public double PayloadHeight;    // m
        public double PayloadWidth;     // m
        public int PayloadParts;

        // Étage atterrisseur (0 = la charge utile se pose seule)
        public double LanderDeltaV;     // m/s que l'atterrisseur doit fournir
        public double LanderGravity;    // m/s², gravité à la surface de la destination
        public double LanderTwr = 2;    // TWR minimal face à cette gravité
        public string LanderBody = "";
        public List<PartInfo> Legs = new List<PartInfo>();   // jambes d'atterrissage débloquées
    }

    /// <summary>
    /// Cherche, pour chaque variante (SSTO, deux étages, boosters à poudre, boosters liquides, asperges),
    /// la combinaison de pièces débloquées la plus légère (ou la moins chère) qui fournit le Δv demandé.
    /// Les quantités d'ergols sont d'abord continues, puis converties en vrais réservoirs.
    /// </summary>
    public class LauncherDesigner
    {
        const double G0 = PartInfo.G0;
        const int MaxEnginesPerStage = 2;    // étages supérieurs : un moteur à vide (deux au plus, comme Centaur)
        const int MaxLowerEngines = 7;       // premier étage (grappe de moteurs)
        const double MaxTankRatio = 0.2;     // masse à vide / masse d'ergols (≈ 0,125 pour les réservoirs du jeu de base)
        const double TankRatioSlack = 1.5;  // tolérance par rapport au meilleur réservoir lors du choix des pièces

        class Mixture
        {
            public string Key;
            public List<PartInfo> Tanks = new List<PartInfo>();
            public List<PartInfo> Adapters = new List<PartInfo>();   // réservoirs à deux diamètres (haut d'un étage plus large)
            public List<string> Sizes = new List<string>();          // diamètres disponibles, du plus petit au plus grand
        }

        const int MaxTankParts = 6;   // au-delà, on passe au diamètre supérieur : base large, étages qui s'affinent

        readonly DesignRequest req;
        readonly Dictionary<string, Mixture> mixtures = new Dictionary<string, Mixture>();
        readonly List<PartInfo> lowerEngines = new List<PartInfo>();
        readonly List<PartInfo> upperEngines = new List<PartInfo>();
        readonly List<PartInfo> solidBoosters = new List<PartInfo>();
        readonly List<PartInfo> decouplers = new List<PartInfo>();
        readonly Dictionary<PartInfo, string> engineMixture = new Dictionary<PartInfo, string>();

        bool heightsKnown;   // l'inventaire vient du jeu : la hauteur d'empilement de chaque pièce est connue

        static bool IsNoseCone(PartInfo p)
        {
            string n = p.Name.ToLowerInvariant();
            return n.Contains("nose") || n.Contains("cone");
        }

        public LauncherDesigner(DesignRequest request)
        {
            req = request;
            heightsKnown = request.Parts.Exists(p => p.HeightM > 0);
            Index();
            separator = PickSeparator(request.Parts);
        }

        readonly PartInfo separator;

        /// <summary>
        /// Fusée de séparation débloquée : petit propulseur à poudre à fixer en surface, qui brûle quelques secondes.
        /// Le Sepratron I est préféré, puis ce qui porte « sep » dans son nom, puis le plus léger.
        /// </summary>
        public static PartInfo PickSeparator(List<PartInfo> parts)
        {
            PartInfo best = null;
            int bestRank = -1;
            foreach (PartInfo p in parts)
            {
                if (!p.Unlocked || p.Kind != PartKind.Booster || (p.Sizes ?? "").Trim() != "srf") continue;
                // Une vraie fusée de séparation est minuscule : on écarte les « packs » de séparation longs comme un booster
                // et les pièces retirées de leur mod.
                if (p.WetMassT > 0.1 || p.Title.IndexOf("Deprecated", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                double burn = p.BurnTimeS;
                if (p.ThrustVacKN <= 0 || p.ThrustVacKN > 100 || burn < 1 || burn > 15) continue;
                string n = (p.Name + " " + p.Title).ToLowerInvariant();
                int rank = p.Name == "sepMotor1" ? 2 : n.Contains("sep") ? 1 : 0;
                if (rank > bestRank || (rank == bestRank && p.WetMassT < best.WetMassT)) { best = p; bestRank = rank; }
            }
            return best;
        }

        /// <summary>
        /// Nombre de fusées de séparation par booster : de quoi écarter le booster vide d'environ 12 m/s,
        /// au moins une en haut et une en bas, toujours un nombre pair, au plus 8.
        /// </summary>
        public static int SeparatorCount(PartInfo sep, double boosterDryT)
        {
            if (sep == null) return 0;
            double impulse = sep.ThrustVacKN * sep.BurnTimeS * 0.94;   // kN·s, inclinées de 20° vers l'arrière
            int n = impulse > 0 ? (int)Math.Ceiling(boosterDryT * 12 / impulse) : 2;
            n = Math.Max(2, n + (n & 1));
            return Math.Min(8, n);
        }

        void AssignSeparators(LauncherDesign d)
        {
            BoosterPlan b = d.Stages.Count > 0 ? d.Stages[0].Boosters : null;
            if (b == null || b.Count == 0) return;
            b.Separator = separator;
            b.SeparatorsEach = SeparatorCount(separator, b.Part.DryMassT + (b.Solid ? 0 : b.TankDry));
        }

        // ---------- Préparation des pièces ----------

        public static string SizeOf(PartInfo p)
        {
            foreach (string raw in (p.Sizes ?? "").Split(','))
            {
                string s = raw.Trim();
                if (s.Length > 0 && s != "srf") return s;
            }
            return "";
        }

        static int StackSizes(PartInfo p)
        {
            int n = 0;
            foreach (string raw in (p.Sizes ?? "").Split(','))
            {
                string s = raw.Trim();
                if (s.Length > 0 && s != "srf") n++;
            }
            return n;
        }

        static bool HasSize(PartInfo p, string size)
        {
            foreach (string raw in (p.Sizes ?? "").Split(','))
                if (raw.Trim() == size) return true;
            return false;
        }

        static double Propellant(PartInfo p)
        {
            return Math.Max(0, p.WetMassT - p.DryMassT);
        }

        static string MixtureKey(IEnumerable<string> names)
        {
            List<string> list = new List<string>();
            foreach (string n in names)
            {
                string t = n.Trim();
                if (t.Length > 0 && !list.Contains(t)) list.Add(t);
            }
            list.Sort(StringComparer.Ordinal);
            return string.Join("+", list.ToArray());
        }

        static readonly HashSet<string> Propellants = new HashSet<string>
        {
            "LiquidFuel", "Oxidizer", "LqdHydrogen", "LqdMethane", "LqdOxygen", "Kerosene", "Hydrazine",
        };

        void Index()
        {
            foreach (PartInfo p in req.Parts)
            {
                if (!p.Unlocked) continue;
                if (p.Kind == PartKind.Tank)
                {
                    // Seulement de vrais réservoirs : on écarte les pièces dont le carburant n'est qu'un complément
                    // (capsules, compartiments, jupes…), trop lourdes pour la quantité emportée.
                    if (Propellant(p) <= 0 || p.DryMassT / Propellant(p) > MaxTankRatio * 1.3) continue;
                    // Coiffes et pièces à un seul nœud d'empilement : rien ne peut se fixer dessous.
                    if (IsNoseCone(p) || (heightsKnown && p.HeightM <= 0)) continue;
                    // Modèle nettement plus étroit que son profil : il casserait l'allure du lanceur (étage large sur étage fin).
                    if (p.DiameterM > 0 && p.DiameterM < 0.8 * CraftBuilder.DiameterOf(SizeOf(p))) continue;
                    // Les adaptateurs (plusieurs diamètres) ne s'empilent pas en série : ils servent seulement à raccorder deux étages.
                    bool adapter = StackSizes(p) == 2;
                    if (!adapter && (StackSizes(p) != 1 || p.DryMassT / Propellant(p) > MaxTankRatio)) continue;
                    List<string> names = new List<string>();
                    foreach (ResourceAmount r in p.Resources)
                        if (Propellants.Contains(r.Name) && r.Amount > 0) names.Add(r.Name);
                    if (names.Count == 0) continue;
                    string key = MixtureKey(names);
                    Mixture mix;
                    if (!mixtures.TryGetValue(key, out mix)) mixtures[key] = mix = new Mixture { Key = key };
                    if (adapter) { mix.Adapters.Add(p); continue; }
                    mix.Tanks.Add(p);
                    if (!mix.Sizes.Contains(SizeOf(p))) mix.Sizes.Add(SizeOf(p));
                }
                else if (p.Kind == PartKind.Decoupler && IsRealDecoupler(p)) decouplers.Add(p);
                // Un propulseur à poudre qui brûle moins de 15 s (tour de sauvetage, fusée de séparation) n'est pas un booster.
                else if (p.Kind == PartKind.Booster && p.ThrustAslKN > 0 && p.IspVac > 0 && Propellant(p) > 0 && p.BurnTimeS >= 15)
                    solidBoosters.Add(p);
            }

            foreach (PartInfo p in req.Parts)
            {
                if (!p.Unlocked || p.Kind != PartKind.Engine || p.ThrustVacKN <= 0 || p.IspVac <= 0) continue;
                if (SizeOf(p).Length == 0) continue;   // moteurs radiaux : pas pour un étage empilé
                string key = MixtureKey((p.Propellants ?? "").Split('+'));
                if (!mixtures.ContainsKey(key)) continue;
                engineMixture[p] = key;
                upperEngines.Add(p);
                if (p.IspAsl > 50 && p.ThrustAslKN > 0) lowerEngines.Add(p);
            }

            foreach (Mixture m in mixtures.Values)
                m.Sizes.Sort((a, b) => CraftBuilder.DiameterOf(a).CompareTo(CraftBuilder.DiameterOf(b)));
            Prune(upperEngines, false);
            Prune(lowerEngines, true);
            PruneBoosters();
        }

        // Retire les moteurs dominés : même ergols et même taille, mais moins de poussée, moins d'Isp et plus lourds.
        void Prune(List<PartInfo> engines, bool atmosphere)
        {
            List<PartInfo> keep = new List<PartInfo>();
            foreach (PartInfo a in engines)
            {
                bool dominated = false;
                foreach (PartInfo b in engines)
                {
                    if (a == b || engineMixture[a] != engineMixture[b] || SizeOf(a) != SizeOf(b)) continue;
                    double ia = atmosphere ? a.IspAsl : a.IspVac, ib = atmosphere ? b.IspAsl : b.IspVac;
                    double ta = atmosphere ? a.ThrustAslKN : a.ThrustVacKN, tb = atmosphere ? b.ThrustAslKN : b.ThrustVacKN;
                    bool better = ib >= ia && b.IspVac >= a.IspVac && tb >= ta && b.DryMassT <= a.DryMassT;
                    bool strictly = ib > ia || b.IspVac > a.IspVac || tb > ta || b.DryMassT < a.DryMassT;
                    if (better && (strictly || string.CompareOrdinal(b.Name, a.Name) < 0)) { dominated = true; break; }
                }
                if (!dominated) keep.Add(a);
            }
            engines.Clear();
            engines.AddRange(keep);
        }

        void PruneBoosters()
        {
            List<PartInfo> keep = new List<PartInfo>();
            foreach (PartInfo a in solidBoosters)
            {
                bool dominated = false;
                foreach (PartInfo b in solidBoosters)
                {
                    if (a == b) continue;
                    bool better = b.ThrustAslKN >= a.ThrustAslKN && b.IspAsl >= a.IspAsl && Propellant(b) >= Propellant(a) && b.DryMassT <= a.DryMassT;
                    bool strictly = b.ThrustAslKN > a.ThrustAslKN || b.IspAsl > a.IspAsl || Propellant(b) > Propellant(a) || b.DryMassT < a.DryMassT;
                    if (better && (strictly || string.CompareOrdinal(b.Name, a.Name) < 0)) { dominated = true; break; }
                }
                if (!dominated) keep.Add(a);
            }
            solidBoosters.Clear();
            solidBoosters.AddRange(keep);
        }

        readonly Dictionary<string, List<PartInfo>> tanksCache = new Dictionary<string, List<PartInfo>>();
        readonly Dictionary<string, double> ratioCache = new Dictionary<string, double>();
        readonly Dictionary<string, double> costCache = new Dictionary<string, double>();

        List<PartInfo> TanksFor(string mixture, string size)
        {
            string key = mixture + "|" + size;
            List<PartInfo> cached;
            if (!tanksCache.TryGetValue(key, out cached)) tanksCache[key] = cached = FindTanks(mixture, size);
            return cached;
        }

        List<PartInfo> FindTanks(string mixture, string size)
        {
            Mixture mix;
            if (!mixtures.TryGetValue(mixture, out mix)) return new List<PartInfo>();
            List<PartInfo> sized = mix.Tanks.FindAll(t => HasSize(t, size));
            return sized.Count > 0 ? sized : mix.Tanks;
        }

        // Rapport masse à vide / masse d'ergols du meilleur réservoir de ce type et de cette taille.
        double TankRatio(string mixture, string size)
        {
            string key = mixture + "|" + size;
            double cached;
            if (ratioCache.TryGetValue(key, out cached)) return cached;
            return ratioCache[key] = ComputeTankRatio(mixture, size);
        }

        // Réservoirs utilisables avec un moteur de cette taille : du même diamètre ou plus larges.
        List<PartInfo> TanksAtLeast(string mixture, string size)
        {
            Mixture mix;
            if (!mixtures.TryGetValue(mixture, out mix)) return new List<PartInfo>();
            double d = CraftBuilder.DiameterOf(size);
            List<PartInfo> list = mix.Tanks.FindAll(t => CraftBuilder.DiameterOf(SizeOf(t)) >= d - 0.01);
            return list.Count > 0 ? list : mix.Tanks;
        }

        double ComputeTankRatio(string mixture, string size)
        {
            double best = BestRatio(TanksAtLeast(mixture, size));
            return best == double.MaxValue ? 0.125 : best;
        }

        /// <summary>
        /// Meilleur rapport masse à vide / ergols parmi les réservoirs qui comptent : un petit réservoir exceptionnellement léger
        /// (quelques centaines de kilos) ne doit pas faire croire que tout l'étage sera aussi léger.
        /// </summary>
        static double BestRatio(List<PartInfo> tanks)
        {
            Dictionary<string, double> largest = new Dictionary<string, double>();
            foreach (PartInfo t in tanks)
            {
                string size = SizeOf(t);
                double v;
                if (!largest.TryGetValue(size, out v) || Propellant(t) > v) largest[size] = Propellant(t);
            }
            double best = double.MaxValue;
            foreach (PartInfo t in tanks)
                if (Propellant(t) >= 0.25 * largest[SizeOf(t)]) best = Math.Min(best, t.DryMassT / Propellant(t));
            return best;
        }

        double TankCostPerTonne(string mixture, string size)
        {
            string key = mixture + "|" + size;
            double cached;
            if (costCache.TryGetValue(key, out cached)) return cached;
            return costCache[key] = ComputeTankCost(mixture, size);
        }

        double ComputeTankCost(string mixture, string size)
        {
            double best = double.MaxValue, ratio = double.MaxValue;
            foreach (PartInfo t in TanksAtLeast(mixture, size))
            {
                double r = t.DryMassT / Propellant(t);
                if (r < ratio) { ratio = r; best = t.Cost / Propellant(t); }
            }
            return best == double.MaxValue ? 0 : best;
        }

        /// <summary>
        /// Beaucoup de pièces savent se détacher (boucliers thermiques, capsules, parachutes, coiffes…) : on ne garde que
        /// celles rangées dans la catégorie « Couplage » de l'éditeur, ou, sans catégorie connue, celles qui s'appellent découpleur.
        /// </summary>
        static bool IsRealDecoupler(PartInfo p)
        {
            if (!string.IsNullOrEmpty(p.Category)) return p.Category == "Coupling";
            string t = (p.Title + " " + p.Name).ToLowerInvariant();
            return t.Contains("decoupl") || t.Contains("découpl") || t.Contains("separator") || t.Contains("séparateur") || t.Contains("interstage");
        }

        PartInfo StackDecoupler(string size)
        {
            PartInfo best = null;
            foreach (PartInfo d in decouplers)
            {
                if (!HasSize(d, size)) continue;
                if (best == null || d.DryMassT < best.DryMassT) best = d;
            }
            if (best != null) return best;
            foreach (PartInfo d in decouplers)
                if (SizeOf(d).Length > 0 && (best == null || d.DryMassT < best.DryMassT)) best = d;
            return best;
        }

        // Les plus petits découpleurs radiaux cèdent sous de gros boosters : on prend le plus robuste qui reste léger.
        PartInfo RadialDecoupler()
        {
            PartInfo best = null;
            foreach (PartInfo d in decouplers)
            {
                if (SizeOf(d).Length > 0) continue;
                if (best == null || (d.DryMassT <= 0.1 && (best.DryMassT > 0.1 || d.DryMassT > best.DryMassT)) ||
                    (best.DryMassT > 0.1 && d.DryMassT < best.DryMassT)) best = d;
            }
            return best;
        }

        // ---------- Simulation ----------

        double Isp(PartInfo e, bool atmosphere)
        {
            return atmosphere ? (e.IspAsl + e.IspVac) / 2 : e.IspVac;
        }

        static double MassFlow(PartInfo e)
        {
            return e.ThrustVacKN / (e.IspVac * G0);   // t/s, constant quelle que soit la pression
        }

        /// <summary>Calcule Δv, TWR et masses de chaque étage. Renvoie le Δv total, ou -1 si la configuration est incohérente.</summary>
        public double Simulate(LauncherDesign d)
        {
            double mass = req.PayloadMass;
            foreach (StagePlan s in d.Stages) mass += s.TotalMass;
            d.TotalMass = mass;
            d.LauncherMass = mass - req.PayloadMass;
            double total = 0;

            for (int i = 0; i < d.Stages.Count; i++)
            {
                StagePlan s = d.Stages[i];
                bool atmo = i == 0 && req.HomeAtmosphere;
                s.MassStart = mass;
                double mdotC = s.EngineCount * MassFlow(s.Engine);
                double thrC = mdotC * Isp(s.Engine, atmo) * G0;
                double liftThrust = s.EngineCount * (i == 0 && req.HomeAtmosphere ? s.Engine.ThrustAslKN : s.Engine.ThrustVacKN);
                double dv = 0;
                double coreFuel = s.Fuel;
                s.StartDeltaV = total;

                s.Twr = liftThrust / (s.MassStart * req.HomeGravity);
                s.CoreTwr = 0;
                BoosterPlan b = s.Boosters;
                if (b != null && b.Count > 0)
                {
                    double mdotB = MassFlow(b.Part);
                    double thrB = mdotB * Isp(b.Part, atmo) * G0;
                    liftThrust += b.Count * (i == 0 && req.HomeAtmosphere ? b.Part.ThrustAslKN : b.Part.ThrustVacKN);
                    s.Twr = liftThrust / (s.MassStart * req.HomeGravity);
                    if (b.Crossfeed)
                    {
                        // Asperges : toutes les paires alimentent les moteurs, la paire extérieure se vide en premier.
                        for (int active = b.Count; active > 0; active -= 2)
                        {
                            double pairFuel = 2 * b.FuelEach;
                            double m1 = mass - pairFuel;
                            dv += (thrC + active * thrB) / (mdotC + active * mdotB) * Math.Log(mass / m1);
                            mass = m1 - 2 * b.DryEach;
                        }
                    }
                    else
                    {
                        // Boosters indépendants : ils brûlent en même temps que le corps central puis sont largués.
                        double tB = b.FuelEach / mdotB;
                        if (mdotC * tB > coreFuel)
                        {
                            // Le corps central s'éteint le premier : les boosters finissent seuls, puis tout l'étage est largué.
                            double tC = coreFuel / mdotC;
                            double m1 = mass - (mdotC + b.Count * mdotB) * tC;
                            dv += (thrC + b.Count * thrB) / (mdotC + b.Count * mdotB) * Math.Log(mass / m1);
                            double m2 = m1 - b.Count * (b.FuelEach - mdotB * tC);
                            dv += thrB / mdotB * Math.Log(m1 / m2);
                            double boosterThrust = b.Count * (atmo ? (b.Part.ThrustAslKN + b.Part.ThrustVacKN) / 2 : b.Part.ThrustVacKN);
                            s.DeltaV = dv;
                            s.CoreTwr = boosterThrust / (m1 * req.HomeGravity);
                            s.CoreStartDeltaV = total + dv;
                            total += dv;
                            mass = m2 - b.Count * b.DryEach - s.DryMass;
                            continue;
                        }
                        double m1b = mass - (mdotC + b.Count * mdotB) * tB;
                        dv += (thrC + b.Count * thrB) / (mdotC + b.Count * mdotB) * Math.Log(mass / m1b);
                        coreFuel -= mdotC * tB;
                        mass = m1b - b.Count * b.DryEach;
                    }
                    // Poussée du corps central entre vide et niveau de la mer, à mi-montée.
                    double coreThrust = s.EngineCount * (atmo ? (s.Engine.ThrustAslKN + s.Engine.ThrustVacKN) / 2 : s.Engine.ThrustVacKN);
                    s.CoreTwr = coreThrust / (mass * req.HomeGravity);
                    s.CoreStartDeltaV = total + dv;
                }

                double end = mass - coreFuel;
                if (end <= 0) return -1;
                dv += thrC / mdotC * Math.Log(mass / end);
                mass = end - s.DryMass;

                s.Twr = liftThrust / (s.MassStart * req.HomeGravity);
                s.DeltaV = dv;
                total += dv;
            }

            d.DeltaV = total;
            d.LiftoffTwr = d.Stages.Count > 0 ? d.Stages[0].Twr : 0;
            d.Cost = CostOf(d);
            return total;
        }

        double CostOf(LauncherDesign d)
        {
            double total = 0;
            foreach (StagePlan s in d.Stages)
            {
                double cost = s.EngineCount * s.Engine.Cost;
                if (s.Tanks.Count > 0) foreach (TankUse t in s.Tanks) cost += t.Count * t.Tank.Cost;
                else cost += s.Fuel * TankCostPerTonne(s.Mixture, s.Size);
                if (s.Decoupler != null) cost += s.Decoupler.Cost;
                BoosterPlan b = s.Boosters;
                if (b != null)
                {
                    double each = b.Part.Cost + (b.Decoupler != null ? b.Decoupler.Cost : 0);
                    if (!b.Solid)
                    {
                        if (b.Tanks.Count > 0) foreach (TankUse t in b.Tanks) each += t.Count * t.Tank.Cost;
                        else each += b.Fuel * TankCostPerTonne(b.Mixture, SizeOf(b.Part));
                    }
                    cost += b.Count * each;
                }
                s.Cost = cost;
                total += cost;
            }
            return total;
        }

        bool Constraints(LauncherDesign d)
        {
            return ConstraintProblem(d) == null;
        }

        /// <summary>La première contrainte non respectée, ou null.</summary>
        // Pendant la recherche, on exige un peu plus de TWR : l'arrondi aux vrais réservoirs ajoute de la masse.
        double twrMargin = 1.04;

        string ConstraintProblem(LauncherDesign d)
        {
            if (d.Stages.Count == 0) return "aucun étage";
            if (d.Stages[0].Twr < req.MinLiftoffTwr * twrMargin) return string.Format("TWR au décollage trop faible ({0:0.00})", d.Stages[0].Twr);
            for (int i = 1; i < d.Stages.Count; i++)
                if (d.Stages[i].Twr < MinTwr(d.Stages[i].StartDeltaV) * twrMargin)
                    return string.Format("TWR de l'étage {0} trop faible ({1:0.00})", i + 1, d.Stages[i].Twr);
            foreach (StagePlan s in d.Stages)
                if (s.Boosters != null && s.Boosters.Count > 0 && s.CoreTwr < MinTwr(s.CoreStartDeltaV) * twrMargin)
                    return string.Format("TWR du corps central trop faible après le largage des boosters ({0:0.00})", s.CoreTwr);
            // Montage possible : la grappe tient sous le plus large réservoir débloqué, les boosters tiennent autour du corps.
            foreach (StagePlan s in d.Stages)
            {
                if (s.EngineCount > 1 && !ClusterFits(s.Mixture, s.Engine, s.EngineCount))
                    return string.Format("{0} moteurs ne tiennent pas sous le plus large réservoir débloqué", s.EngineCount);
                BoosterPlan b = s.Boosters;
                if (b != null && b.Count > 0)
                {
                    double core = CraftBuilder.DiameterOf(LargestSize(s.Mixture, s.Size));
                    double each = CraftBuilder.DiameterOf(SizeOf(b.Part));
                    if (b.Count * each > Math.PI * (core + each) * 0.95)
                        return string.Format("{0} boosters ne tiennent pas autour du corps central", b.Count);
                }
            }
            // Un premier étage qui ne fournit presque rien n'est qu'un SSTO déguisé.
            if (d.Stages.Count > 1 && d.Stages[0].DeltaV < MinFirstStageShare * req.TargetDeltaV)
                return "premier étage trop petit";
            // Un étage supérieur qui n'apporte presque rien ne vaut pas son découpleur (sur les vrais lanceurs, 30 à 50 %).
            for (int i = 1; i < d.Stages.Count; i++)
                if (d.Stages[i].DeltaV < 0.15 * req.TargetDeltaV) return "étage supérieur trop petit";
            return null;
        }

        /// <summary>
        /// TWR minimal d'un étage supérieur : un étage allumé en pleine montée doit encore lutter contre la gravité,
        /// un étage allumé une fois en orbite peut être poussif.
        /// </summary>
        double MinTwr(double startDeltaV)
        {
            // De 0,9 à mi-montée jusqu'au minimum choisi une fois la vitesse orbitale atteinte, sans palier.
            double x = (startDeltaV / req.AscentDeltaV - 0.5) / 0.5;
            if (!req.HomeAtmosphere) x = startDeltaV / (req.AscentDeltaV * 0.5);
            x = Math.Max(0, Math.Min(1, x));
            double high = Math.Max(0.9, req.MinUpperTwr);
            return high + (req.MinUpperTwr - high) * x;
        }

        /// <summary>
        /// Masse (ou coût) à minimiser. Chaque moteur en plus d'un par étage coûte 2 % : une grappe ne l'emporte sur un seul
        /// gros moteur que si elle est nettement plus efficace.
        /// </summary>
        double Score(LauncherDesign d)
        {
            int extra = 0;
            foreach (StagePlan s in d.Stages) extra += Math.Max(0, s.EngineCount - 1);
            return (req.MinimizeCost ? d.Cost : d.TotalMass) * (1 + ClusterPenalty * extra);
        }

        const double ClusterPenalty = 0.02;

        // ---------- Construction des étages ----------

        StagePlan MakeStage(PartInfo engine, int count, double fuel, bool withDecoupler)
        {
            StagePlan s = new StagePlan
            {
                Engine = engine,
                EngineCount = count,
                Mixture = engineMixture[engine],
                Fuel = fuel,
            };
            s.Size = ClusterSize(s.Mixture, engine, count);
            s.TankDry = fuel * TankRatio(s.Mixture, s.Size);
            if (withDecoupler)
            {
                s.Decoupler = StackDecoupler(s.Size);
                s.DecouplerMass = s.Decoupler != null ? s.Decoupler.DryMassT : 0.05;
            }
            return s;
        }

        bool ClusterFits(string mixture, PartInfo engine, int count)
        {
            return ClusterCapacity(CraftBuilder.DiameterOf(LargestSize(mixture, SizeOf(engine))), CraftBuilder.DiameterOf(SizeOf(engine))) >= count;
        }

        string LargestSize(string mixture, string fallback)
        {
            Mixture mix;
            if (!mixtures.TryGetValue(mixture, out mix) || mix.Sizes.Count == 0) return fallback;
            string largest = mix.Sizes[mix.Sizes.Count - 1];
            if (req.MaxWidth > 0)
                for (int i = mix.Sizes.Count - 1; i >= 0; i--)
                    if (CraftBuilder.DiameterOf(mix.Sizes[i]) <= req.MaxWidth) return mix.Sizes[i];
            return largest;
        }

        /// <summary>Plus petit diamètre de réservoir sous lequel tient la grappe de moteurs.</summary>
        string ClusterSize(string mixture, PartInfo engine, int count)
        {
            string size = SizeOf(engine);
            Mixture mix;
            if (count <= 1 || !mixtures.TryGetValue(mixture, out mix) || mix.Sizes.Count == 0) return size;
            double engineD = CraftBuilder.DiameterOf(size);
            foreach (string s in mix.Sizes)
                if (ClusterCapacity(CraftBuilder.DiameterOf(s), engineD) >= count) return s;
            return mix.Sizes[mix.Sizes.Count - 1];
        }

        void SetFuel(StagePlan s, double fuel)
        {
            s.Fuel = fuel;
            s.TankDry = fuel * TankRatio(s.Mixture, s.Size);
            BoosterPlan b = s.Boosters;
            if (b != null && !b.Solid)
            {
                b.Fuel = fuel / Math.Max(1, s.EngineCount);   // un booster liquide = un moteur du corps central et sa part d'ergols
                b.TankDry = b.Fuel * TankRatio(b.Mixture, SizeOf(b.Part));
            }
        }

        /// <summary>Trouve la quantité minimale d'ergols du premier étage qui atteint le Δv visé.</summary>
        bool SolveFirstStage(LauncherDesign d, bool checkTwr = true)
        {
            StagePlan s = d.Stages[0];
            // Le TWR au décollage baisse quand on ajoute des ergols : s'il est déjà trop faible à vide, inutile de chercher.
            SetFuel(s, 0.001);
            Simulate(d);
            if (checkTwr && s.Twr < req.MinLiftoffTwr) return false;

            double lo = 0, hi = 4000;
            SetFuel(s, hi);
            if (Simulate(d) < req.TargetDeltaV) return false;
            while (hi - lo > Math.Max(0.02, hi * 0.002))
            {
                double mid = (lo + hi) / 2;
                SetFuel(s, mid);
                double dv = Simulate(d);
                if (dv >= req.TargetDeltaV) hi = mid; else lo = mid;
            }
            SetFuel(s, hi);
            Simulate(d);
            return true;
        }

        class Upper
        {
            public PartInfo Engine;
            public int Count;
            public double Fuel;
            public double Mass;
            public double DeltaV;
        }

        // Étages supérieurs possibles (Δv, masse) ; on ne garde que ceux qui ne sont pas dominés.
        List<Upper> UpperFront()
        {
            List<Upper> all = new List<Upper>();
            double payload = Math.Max(0.1, req.PayloadMass);
            foreach (PartInfo e in upperEngines)
                for (int n = 1; n <= MaxEnginesPerStage; n++)
                    for (int k = 0; k < 28; k++)
                    {
                        double fuel = payload * 0.05 * Math.Pow(1.35, k);
                        StagePlan s = MakeStage(e, n, fuel, true);
                        double m0 = req.PayloadMass + s.TotalMass;
                        double m1 = m0 - fuel;
                        double dv = e.IspVac * G0 * Math.Log(m0 / m1);
                        if (dv > req.TargetDeltaV * 0.9) break;
                        if (n * e.ThrustVacKN / (m0 * req.HomeGravity) < req.MinUpperTwr) break;
                        all.Add(new Upper { Engine = e, Count = n, Fuel = fuel, Mass = m0, DeltaV = dv });
                    }

            all.Sort((a, b) => a.Mass.CompareTo(b.Mass));
            List<Upper> front = new List<Upper>();
            double bestDv = -1;
            foreach (Upper u in all)
                if (u.DeltaV > bestDv + 25) { front.Add(u); bestDv = u.DeltaV; }

            // Pour garder un temps de calcul raisonnable, on échantillonne le front.
            if (front.Count > 24)
            {
                List<Upper> sample = new List<Upper>();
                for (int i = 0; i < 24; i++) sample.Add(front[i * (front.Count - 1) / 23]);
                front = sample;
            }
            return front;
        }

        LauncherDesign NewDesign(string variant, string description)
        {
            return new LauncherDesign { Variant = variant, Description = description };
        }

        // Meilleurs candidats de la variante en cours : si l'arrondi aux vrais réservoirs fait échouer le premier,
        // on essaie les suivants.
        readonly List<LauncherDesign> pool = new List<LauncherDesign>();
        const int PoolSize = 12;

        // Raisons de rejet pendant la recherche de la variante en cours, pour expliquer un échec.
        readonly Dictionary<string, int> rejections = new Dictionary<string, int>();

        LauncherDesign Better(LauncherDesign best, LauncherDesign candidate)
        {
            string problem = ConstraintProblem(candidate);
            if (problem != null)
            {
                int paren = problem.IndexOf(" (");
                string key = paren > 0 ? problem.Substring(0, paren) : problem;
                int n;
                rejections.TryGetValue(key, out n);
                rejections[key] = n + 1;
                return best;
            }
            double score = Score(candidate);
            bool forPool = pool.Count < PoolSize || score < Score(pool[pool.Count - 1]);
            bool forBest = best == null || score < Score(best);
            if (!forPool && !forBest) return best;
            LauncherDesign copy = Clone(candidate);
            Simulate(copy);   // recalcule masses, coût et Δv sur la copie
            if (forPool)
            {
                pool.Add(copy);
                pool.Sort((a, b) => Score(a).CompareTo(Score(b)));
                if (pool.Count > PoolSize) pool.RemoveAt(pool.Count - 1);
            }
            return forBest ? copy : best;
        }

        static LauncherDesign Clone(LauncherDesign d)
        {
            LauncherDesign c = new LauncherDesign { Variant = d.Variant, Description = d.Description };
            foreach (StagePlan s in d.Stages)
            {
                StagePlan n = (StagePlan)s.GetType().GetMethod("MemberwiseClone",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(s, null);
                n.Tanks = new List<TankUse>(s.Tanks);
                if (s.Boosters != null)
                {
                    BoosterPlan b = s.Boosters;
                    n.Boosters = new BoosterPlan
                    {
                        Count = b.Count, Solid = b.Solid, Crossfeed = b.Crossfeed, Part = b.Part, Mixture = b.Mixture,
                        Tanks = new List<TankUse>(b.Tanks), Fuel = b.Fuel, TankDry = b.TankDry,
                        Decoupler = b.Decoupler, DecouplerMass = b.DecouplerMass,
                    };
                }
                c.Stages.Add(n);
            }
            return c;
        }

        // ---------- Variantes ----------

        public string Stats
        {
            get
            {
                return string.Format("{0} moteurs de décollage, {1} moteurs d'étage supérieur, {2} propulseurs à poudre, {3} types d'ergols",
                    lowerEngines.Count, upperEngines.Count, solidBoosters.Count, mixtures.Count);
            }
        }

        public List<LauncherDesign> Run()
        {
            if (req.LanderDeltaV <= 0) return RunLauncher();

            PartInfo legs;
            string problem;
            StagePlan lander = DesignLander(out legs, out problem);
            if (lander == null)
            {
                List<LauncherDesign> none = new List<LauncherDesign>();
                foreach (string[] v in Variants)
                    none.Add(new LauncherDesign { Variant = v[0], Description = v[1], Problem = problem });
                return none;
            }
            int legCount = legs != null ? LanderLegs : 0;
            double mass = req.PayloadMass, height = req.PayloadHeight, width = req.PayloadWidth;
            int parts = req.PayloadParts;
            // Le lanceur emporte la charge utile et son atterrisseur.
            req.PayloadMass += lander.TotalMass + legCount * (legs != null ? legs.DryMassT : 0);
            req.PayloadHeight += TanksHeight(lander.Tanks) + HeightOf(lander.Engine);
            req.PayloadWidth = Math.Max(req.PayloadWidth, CraftBuilder.DiameterOf(lander.Size) + 1);
            req.PayloadParts += TankParts(lander.Tanks) + lander.EngineCount + legCount;
            try
            {
                List<LauncherDesign> result = RunLauncher();
                foreach (LauncherDesign d in result)
                {
                    d.Lander = lander;
                    d.LanderLegs = legs;
                    d.LanderLegCount = legCount;
                }
                return result;
            }
            finally
            {
                req.PayloadMass = mass;
                req.PayloadHeight = height;
                req.PayloadWidth = width;
                req.PayloadParts = parts;
            }
        }

        static readonly string[][] Variants =
        {
            new[] { "SSTO", "Un seul étage, sans largage." },
            new[] { "Deux étages", "Deux étages empilés, sans boosters." },
            new[] { "Boosters à poudre", "Deux étages et des propulseurs à poudre largués en vol." },
            new[] { "Boosters liquides", "Deux étages et des boosters liquides largués ensemble." },
            new[] { "Asperges", "Boosters liquides largués 2 par 2 ; chaque paire alimente le reste du lanceur avant d'être larguée." },
        };

        // ---------- Étage atterrisseur ----------

        const int LanderLegs = 4;
        const int MaxLanderEngines = 4;

        /// <summary>
        /// Jambes d'atterrissage : la plus légère qui porte la masse posée (environ 0,4 % de cette masse par jambe,
        /// comme les jambes du jeu de base), sinon la plus robuste.
        /// </summary>
        PartInfo PickLegs(double landedMass)
        {
            PartInfo best = null, heaviest = null;
            foreach (PartInfo l in req.Legs)
            {
                if (heaviest == null || l.DryMassT > heaviest.DryMassT) heaviest = l;
                if (l.DryMassT >= 0.004 * landedMass && (best == null || l.DryMassT < best.DryMassT)) best = l;
            }
            return best ?? heaviest;
        }

        /// <summary>
        /// Étage qui pose la charge utile et assure ce qu'elle ne peut pas faire seule (descente, puis remontée et retour
        /// si la mission en comporte) : un ou quelques moteurs à vide, poussée suffisante face à la gravité de la destination,
        /// ergols qui se conservent (pas d'hydrogène liquide, qui s'évapore pendant le trajet).
        /// </summary>
        StagePlan DesignLander(out PartInfo legs, out string problem)
        {
            legs = null;
            problem = "Aucun moteur débloqué ne peut poser la charge utile sur " + req.LanderBody + ".";
            StagePlan best = null;
            double bestScore = double.MaxValue;
            bool tooWeak = false;
            foreach (PartInfo e in upperEngines)
            {
                string mix = engineMixture[e];
                if (mix.Contains("LqdHydrogen")) continue;
                for (int n = 1; n <= MaxLanderEngines; n++)
                {
                    if (n > 1 && !ClusterFits(mix, e, n)) continue;
                    StagePlan s = MakeStage(e, n, 0, false);
                    double ratio = TankRatio(mix, s.Size);
                    double r = Math.Exp(req.LanderDeltaV / (e.IspVac * G0));
                    if (ratio <= 0 || (r - 1) * ratio >= 1) continue;
                    double legMass = 0, fuel = 0, m0 = 0;
                    PartInfo leg = null;
                    for (int iter = 0; iter < 3; iter++)
                    {
                        double fixedMass = req.PayloadMass + n * e.DryMassT + LanderLegs * legMass;
                        fuel = (r - 1) * fixedMass / (1 - (r - 1) * ratio);
                        m0 = fixedMass + fuel * (1 + ratio);
                        leg = PickLegs(m0);
                        legMass = leg != null ? leg.DryMassT : 0;
                    }
                    if (n * e.ThrustVacKN / (m0 * req.LanderGravity) < req.LanderTwr) { tooWeak = true; continue; }
                    double cost = n * e.Cost + fuel * TankCostPerTonne(mix, s.Size);
                    double score = (req.MinimizeCost ? cost : m0) * (1 + ClusterPenalty * (n - 1));
                    if (score >= bestScore) continue;
                    bestScore = score;
                    SetFuel(s, fuel);
                    best = s;
                    legs = leg;
                }
            }
            if (best == null)
            {
                if (tooWeak) problem = string.Format("Aucun moteur débloqué n'a assez de poussée pour poser la charge utile sur {0} (TWR {1:0.#} demandé).",
                    req.LanderBody, req.LanderTwr);
                return null;
            }

            // Vrais réservoirs : on augmente un peu les ergols jusqu'à couvrir le Δv demandé.
            double wanted = best.Fuel;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                // Assez large pour porter la charge utile : un atterrisseur est trapu.
                double minD = Math.Max(CraftBuilder.DiameterOf(ClusterSize(best.Mixture, best.Engine, best.EngineCount)), 0.5 * req.PayloadWidth);
                best.Size = SquatSize(wanted, best.Mixture, minD) ??
                    ChooseSize(wanted, best.Mixture, best.Engine, best.EngineCount, minD, double.MaxValue);
                double got, dry;
                best.Tanks = Fill(wanted, best.Mixture, best.Size, out got, out dry);
                best.Fuel = got;
                best.TankDry = dry;
                if (LanderDeltaV(best, legs) >= req.LanderDeltaV - 1) break;
                wanted *= 1.05;
            }
            best.DeltaV = LanderDeltaV(best, legs);
            best.MassStart = req.PayloadMass + best.TotalMass + LanderLegs * (legs != null ? legs.DryMassT : 0);
            best.Twr = best.EngineCount * best.Engine.ThrustVacKN / (best.MassStart * req.LanderGravity);
            best.Cost = best.EngineCount * best.Engine.Cost + best.Fuel * TankCostPerTonne(best.Mixture, best.Size)
                + LanderLegs * (legs != null ? legs.Cost : 0);
            if (best.Tanks.Count == 0) { problem = "Aucun réservoir débloqué pour l'atterrisseur."; return null; }
            return best;
        }

        /// <summary>Plus petit diamètre où les ergols tiennent en deux réservoirs au plus : un atterrisseur trapu, pas une tour.</summary>
        string SquatSize(double fuel, string mixture, double minD)
        {
            Mixture mix;
            if (!mixtures.TryGetValue(mixture, out mix)) return null;
            foreach (string size in mix.Sizes)
            {
                double d = CraftBuilder.DiameterOf(size);
                if (d < minD - 0.01 || (req.MaxWidth > 0 && d > req.MaxWidth)) continue;
                double got, dry;
                List<TankUse> tanks = Fill(fuel, mixture, size, out got, out dry);
                if (tanks.Count > 0 && TankParts(tanks) <= 2 && got >= fuel * 0.98) return size;
            }
            return null;
        }

        double LanderDeltaV(StagePlan s, PartInfo legs)
        {
            double dry = req.PayloadMass + s.DryMass + LanderLegs * (legs != null ? legs.DryMassT : 0);
            return s.Engine.IspVac * G0 * Math.Log((dry + s.Fuel) / dry);
        }

        List<LauncherDesign> RunLauncher()
        {
            List<LauncherDesign> result = new List<LauncherDesign>();
            if (req.TargetDeltaV <= 0) return result;
            List<Upper> front = UpperFront();
            Trace("front " + front.Count);

            pool.Clear(); rejections.Clear(); Ssto();
            result.Add(Finish("SSTO", "Un seul étage, sans largage."));
            pool.Clear(); rejections.Clear(); TwoStages(front, null);
            result.Add(Finish("Deux étages", "Deux étages empilés, sans boosters."));
            pool.Clear(); rejections.Clear(); TwoStages(front, "solid");
            result.Add(Finish("Boosters à poudre", "Deux étages et des propulseurs à poudre largués en vol."));
            pool.Clear(); rejections.Clear(); TwoStages(front, "liquid");
            result.Add(Finish("Boosters liquides", "Deux étages et des boosters liquides largués ensemble."));
            pool.Clear(); rejections.Clear(); TwoStages(front, "asparagus");
            result.Add(Finish("Asperges",
                "Boosters liquides largués 2 par 2 ; chaque paire alimente le reste du lanceur avant d'être larguée."));
            Trace("fin");
            return result;
        }

        public static Action<string> Log;

        static void Trace(string message)
        {
            if (Log != null) Log(message);
        }

        LauncherDesign Ssto()
        {
            LauncherDesign best = null;
            foreach (PartInfo e in lowerEngines)
                for (int n = 1; n <= MaxLowerEngines + 2; n++)
                {
                    LauncherDesign d = NewDesign("SSTO", "");
                    d.Stages.Add(MakeStage(e, n, 0, false));
                    if (!SolveFirstStage(d)) continue;
                    best = Better(best, d);
                }
            return best;
        }

        class Core
        {
            public List<Upper> Uppers = new List<Upper>();   // les meilleurs étages supérieurs pour ce corps central
            public List<double> Scores = new List<double>();
            public PartInfo Engine;
            public int Count;
            public double Score { get { return Scores.Count > 0 ? Scores[0] : double.MaxValue; } }

            public void Add(Upper u, double score)
            {
                int i = 0;
                while (i < Scores.Count && Scores[i] <= score) i++;
                if (i >= UppersPerCore) return;
                Uppers.Insert(i, u);
                Scores.Insert(i, score);
                if (Uppers.Count > UppersPerCore) { Uppers.RemoveAt(UppersPerCore); Scores.RemoveAt(UppersPerCore); }
            }
        }
        const int UppersPerCore = 3;
        const double MinCoreTwr = 0.5;   // en dessous, même avec des boosters le corps central ne suivrait pas

        // Meilleurs couples (étage supérieur, premier étage) sans tenir compte du TWR au décollage :
        // les boosters servent justement à le corriger.
        readonly List<Core> cores = new List<Core>();
        const int CoresForBoosters = 40;
        const double MinFirstStageShare = 0.25;

        LauncherDesign TwoStages(List<Upper> front, string boosterKind)
        {
            LauncherDesign best = null;
            PartInfo radial = RadialDecoupler();
            if (boosterKind == null)
            {
                cores.Clear();
                foreach (Upper u in front)
                    foreach (PartInfo e in lowerEngines)
                        for (int n = 1; n <= MaxLowerEngines; n++)
                        {
                            LauncherDesign d = Build(u, e, n, null, false);
                            if (d.Stages.Count == 0 || !UpperStagesOk(d)) continue;
                            best = Better(best, d);
                            if (d.Stages[0].DeltaV < MinFirstStageShare * req.TargetDeltaV || d.Stages[0].Twr < MinCoreTwr) continue;
                            // Quelques étages supérieurs par moteur du premier étage, pour garder des corps centraux variés.
                            Core same = cores.Find(c => c.Engine == e && c.Count == n);
                            if (same == null) cores.Add(same = new Core { Engine = e, Count = n });
                            same.Add(u, Score(d));
                        }
                cores.Sort((a, b) => a.Score.CompareTo(b.Score));
                if (cores.Count > CoresForBoosters) cores.RemoveRange(CoresForBoosters, cores.Count - CoresForBoosters);
                return best;
            }

            Trace(boosterKind + " : " + cores.Count + " corps centraux");
            foreach (Core core in cores)
            foreach (Upper u in core.Uppers)
            {
                PartInfo e = core.Engine;
                int n = core.Count;
                if (boosterKind == "solid")
                {
                    foreach (PartInfo srb in solidBoosters)
                        for (int c = 2; c <= 4; c += 2)   // comme Ariane 5, Atlas V, H-IIA : 2 ou 4 boosters
                            best = Better(best, Build(u, e, n, new BoosterPlan
                            {
                                Count = c, Solid = true, Part = srb, Decoupler = radial,
                                DecouplerMass = radial != null ? radial.DryMassT : 0.025,
                            }));
                    continue;
                }
                bool asparagus = boosterKind == "asparagus";
                for (int c = 2; c <= (asparagus ? 6 : 4); c += 2)
                    best = Better(best, Build(u, e, n, new BoosterPlan
                    {
                        Count = c, Solid = false, Crossfeed = asparagus, Part = e, Mixture = engineMixture[e],
                        Decoupler = radial, DecouplerMass = radial != null ? radial.DryMassT : 0.025,
                    }));
            }
            return best;
        }

        bool UpperStagesOk(LauncherDesign d)
        {
            for (int i = 1; i < d.Stages.Count; i++) if (d.Stages[i].Twr < MinTwr(d.Stages[i].StartDeltaV)) return false;
            return true;
        }

        LauncherDesign Build(Upper u, PartInfo lower, int n, BoosterPlan boosters, bool checkTwr = true)
        {
            LauncherDesign d = NewDesign("", "");
            StagePlan first = MakeStage(lower, n, 0, false);
            first.Boosters = boosters;
            d.Stages.Add(first);
            d.Stages.Add(MakeStage(u.Engine, u.Count, u.Fuel, true));
            if (!SolveFirstStage(d, checkTwr)) d.Stages.Clear();
            return d;
        }

        // ---------- Conversion en vraies pièces ----------

        /// <summary>Remplace les quantités continues d'ergols par de vrais réservoirs, du haut vers le bas.</summary>
        LauncherDesign Finish(string variant, string description)
        {
            Trace(variant + " : " + pool.Count + " candidats");
            if (pool.Count == 0)
            {
                LauncherDesign none = NewDesign(variant, description);
                none.Problem = Unavailable(variant);
                return none;
            }
            LauncherDesign first = null;
            foreach (LauncherDesign candidate in pool)
            {
                LauncherDesign d = Quantized(Clone(candidate), variant, description);
                Trace(string.Format("  {0:0.0} t {1}×{2} +{3} {4} → {5}", candidate.TotalMass, candidate.Stages[0].EngineCount, candidate.Stages[0].Engine.Name,
                    candidate.Stages[0].Boosters != null ? candidate.Stages[0].Boosters.Count + "×" + candidate.Stages[0].Boosters.Part.Name : "-",
                    candidate.Stages[1 % candidate.Stages.Count].Engine.Name, d.Valid ? "ok" : d.Problem));
                if (d.Valid) return d;
                if (first == null) first = d;
            }
            return first;
        }

        LauncherDesign Quantized(LauncherDesign d, string variant, string description)
        {
            d.Variant = variant;
            d.Description = description;

            StagePlan first = d.Stages[0];
            double[] fuels = new double[d.Stages.Count];
            for (int i = 0; i < fuels.Length; i++) fuels[i] = d.Stages[i].Fuel;
            // Trop haut pour le VAB : on reprend avec des réservoirs plus larges, donc moins hauts.
            for (widen = 0; widen <= MaxWiden; widen++)
            {
                // Du haut vers le bas : chaque étage est au moins aussi large que celui qu'il porte.
                for (int i = d.Stages.Count - 1; i >= 1; i--)
                {
                    d.Stages[i].Fuel = fuels[i];
                    Quantize(d, i);
                }
                d.PayloadDecoupler = StackDecoupler(d.Stages[d.Stages.Count - 1].Size);
                SolveFirstStage(d);
                double wanted = first.Fuel;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    SetFuel(first, wanted);
                    Quantize(d, 0);
                    double dv = Simulate(d);
                    if (dv >= req.TargetDeltaV) break;
                    wanted = Math.Max(wanted * 1.03, first.Fuel * (1 + (req.TargetDeltaV - dv) / req.TargetDeltaV));
                }
                Measure(d);
                if (req.MaxHeight <= 0 || d.HeightM <= req.MaxHeight) break;
            }
            widen = 0;

            d.Problem = "";
            if (Simulate(d) < 0) { d.Valid = false; return d; }
            twrMargin = 1;
            string problem = ConstraintProblem(d);
            twrMargin = 1.04;
            if (d.DeltaV < req.TargetDeltaV - 1) problem = string.Format("Δv insuffisant une fois les réservoirs arrondis ({0:0} m/s)", d.DeltaV);
            if (problem == null) problem = SiteProblem(d);
            d.Valid = problem == null;
            if (!d.Valid) d.Problem = problem;
            for (int i = 0; i < d.Stages.Count; i++)
            {
                StagePlan s = d.Stages[i];
                if (s.Decoupler == null && s != first)
                    d.Notes.Add("Aucun découpleur débloqué à la bonne taille.");
                if (s.EngineCount > 1 && ClusterCapacity(CraftBuilder.DiameterOf(s.Size), CraftBuilder.DiameterOf(SizeOf(s.Engine))) < s.EngineCount)
                    d.Notes.Add(string.Format("Étage {0} : {1} moteurs se chevauchent sous un réservoir de {2} (pas de réservoir débloqué plus large).",
                        i + 1, s.EngineCount, Diameter(s.Size)));
                if (i + 1 < d.Stages.Count && s.Size != d.Stages[i + 1].Size &&
                    (s.Tanks.Count == 0 || StackSizes(s.Tanks[0].Tank) != 2))
                    d.Notes.Add(string.Format("Aucun réservoir adaptateur {0} → {1} débloqué : ajoute un adaptateur structurel entre les étages {2} et {3}.",
                        Diameter(s.Size), Diameter(d.Stages[i + 1].Size), i + 1, i + 2));
            }
            return d;
        }

        string Unavailable(string variant)
        {
            if (variant == "Boosters à poudre" && solidBoosters.Count == 0) return "Aucun propulseur à poudre débloqué.";
            if (lowerEngines.Count == 0) return "Aucun moteur utilisable au décollage n'est débloqué.";
            string main = null;
            int count = 0;
            foreach (KeyValuePair<string, int> r in rejections)
                if (r.Value > count) { count = r.Value; main = r.Key; }
            return "Aucune combinaison de pièces débloquées n'atteint le Δv demandé" +
                (main != null ? " ; le plus souvent : " + main + "." : " avec un TWR suffisant.");
        }

        public static string Diameter(string size)
        {
            return CraftBuilder.DiameterOf(size).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " m";
        }

        /// <summary>Combien de moteurs de cette taille tiennent sous un réservoir de ce diamètre (grappe, coupleur multiple).</summary>
        static int ClusterCapacity(double tankDiameter, double engineDiameter)
        {
            if (tankDiameter < engineDiameter - 0.01) return 0;
            double k = tankDiameter / engineDiameter;
            return Math.Max(1, (int)Math.Floor(k * k + 1e-9));
        }

        /// <summary>
        /// Remplace les ergols de l'étage par de vrais réservoirs. Le diamètre est le plus petit qui accueille les moteurs,
        /// reste au moins aussi large que l'étage du dessus et tient en peu de réservoirs ; si l'étage du dessus est plus étroit,
        /// un réservoir adaptateur coiffe l'étage.
        /// </summary>
        void Quantize(LauncherDesign d, int index)
        {
            StagePlan s = d.Stages[index];
            string above = index + 1 < d.Stages.Count ? d.Stages[index + 1].Size : null;
            double minD = above != null ? CraftBuilder.DiameterOf(above) : 0;
            s.Size = ChooseSize(s.Fuel, s.Mixture, s.Engine, s.EngineCount, minD, double.MaxValue);

            double fuel = s.Fuel;
            List<TankUse> tanks = new List<TankUse>();
            double dry = 0, got = 0;
            PartInfo adapter = above != null && above != s.Size ? Adapter(s.Mixture, s.Size, above) : null;
            if (adapter != null && Propellant(adapter) < fuel)
            {
                AddTank(tanks, adapter);
                dry += adapter.DryMassT;
                got += Propellant(adapter);
                fuel -= Propellant(adapter);
            }
            double f, td;
            foreach (TankUse t in Fill(fuel, s.Mixture, s.Size, out f, out td)) tanks.Add(t);
            s.Tanks = tanks;
            s.Fuel = got + f;
            s.TankDry = dry + td;
            if (index > 0)
            {
                // Le découpleur sous un étage supérieur prend son diamètre.
                s.Decoupler = StackDecoupler(s.Size) ?? s.Decoupler;
                s.DecouplerMass = s.Decoupler != null ? s.Decoupler.DryMassT : 0.05;
            }

            BoosterPlan b = s.Boosters;
            if (b != null && !b.Solid)
            {
                string bs = ChooseSize(b.Fuel, b.Mixture, b.Part, 1, 0, CraftBuilder.DiameterOf(s.Size));
                // Un booster plus haut que le corps central dépasserait dessous : on l'élargit jusqu'à ce qu'il tienne.
                double coreHeight = TanksHeight(s.Tanks) + HeightOf(s.Engine);
                Mixture bm;
                if (heightsKnown && coreHeight > 0 && mixtures.TryGetValue(b.Mixture, out bm))
                    foreach (string size in bm.Sizes)
                    {
                        double bd = CraftBuilder.DiameterOf(size);
                        if (bd < CraftBuilder.DiameterOf(bs) - 0.01 || bd > CraftBuilder.DiameterOf(s.Size) + 0.01) continue;
                        double bf, btd;
                        List<TankUse> bt = Fill(b.Fuel, b.Mixture, size, out bf, out btd, true);
                        if (bt.Count > 0 && TanksHeight(bt) + HeightOf(b.Part) <= 0.95 * coreHeight) { bs = size; break; }
                    }
                b.Tanks = Fill(b.Fuel, b.Mixture, bs, out b.Fuel, out b.TankDry, true);
            }
        }

        string ChooseSize(double fuel, string mixture, PartInfo engine, int engines, double minDiameter, double maxDiameter)
        {
            Mixture mix;
            string engineSize = SizeOf(engine);
            if (!mixtures.TryGetValue(mixture, out mix) || mix.Sizes.Count == 0) return engineSize;
            double engineD = CraftBuilder.DiameterOf(engineSize);
            if (req.MaxWidth > 0) maxDiameter = Math.Min(maxDiameter, req.MaxWidth);
            List<string> sizes = new List<string>();
            foreach (string size in mix.Sizes)
            {
                double dia = CraftBuilder.DiameterOf(size);
                if (dia < minDiameter - 0.01 || dia > maxDiameter + 0.01) continue;
                if (ClusterCapacity(dia, engineD) < engines) continue;
                sizes.Add(size);
            }
            // Rien ne convient (pas de réservoir assez large) : le plus large disponible.
            if (sizes.Count == 0) return mix.Sizes[mix.Sizes.Count - 1];
            // Parmi les diamètres qui tiennent en peu de réservoirs, celui qui ajoute le moins de masse inutile
            // (masse à vide, ergols en trop une fois arrondis aux vrais réservoirs, et un peu par pièce).
            int chosen = -1, fewest = int.MaxValue, fewestAt = 0;
            double best = double.MaxValue;
            for (int i = 0; i < sizes.Count; i++)
            {
                double got, dry;
                int parts = TankParts(Fill(fuel, mixture, sizes[i], out got, out dry));
                if (parts < fewest) { fewest = parts; fewestAt = i; }
                if (parts > MaxTankParts) continue;
                // Un étage plus large que nécessaire pèse peu, mais l'allure fusée veut des étages qui s'affinent vers le haut.
                double score = FillScore(fuel, got, dry, parts) + 0.03 * fuel * i;
                if (score < best - 1e-9) { best = score; chosen = i; }
            }
            if (chosen < 0) chosen = fewestAt;
            return sizes[Math.Min(chosen + widen, sizes.Count - 1)];
        }

        // ---------- Encombrement (VAB et pas de tir) ----------

        const int MaxWiden = 3;
        int widen;   // crans de diamètre ajoutés quand le lanceur est trop haut

        /// <summary>Hauteur d'empilement d'une pièce ; estimée d'après sa masse quand le jeu ne la donne pas.</summary>
        static double HeightOf(PartInfo p)
        {
            if (p == null) return 0;
            if (p.HeightM > 0) return p.HeightM;
            double dia = CraftBuilder.DiameterOf(SizeOf(p));
            if (p.Kind == PartKind.Decoupler) return 0.4;
            if (p.Kind == PartKind.Engine) return 1.2 * dia;
            double density = p.Kind == PartKind.Booster ? 1.6 : 1.0;   // t/m³ : ordre de grandeur des pièces du jeu
            foreach (ResourceAmount r in p.Resources)
                if (r.Name == "LqdHydrogen") density = 0.4;
            return p.WetMassT / (Math.PI * dia * dia / 4 * density);
        }

        static double TanksHeight(List<TankUse> tanks)
        {
            double h = 0;
            foreach (TankUse t in tanks) h += t.Count * HeightOf(t.Tank);
            return h;
        }

        static int TankParts(List<TankUse> tanks)
        {
            int n = 0;
            foreach (TankUse t in tanks) n += t.Count;
            return n;
        }

        /// <summary>Hauteur, largeur et nombre de pièces du lanceur assemblé, charge utile comprise.</summary>
        void Measure(LauncherDesign d)
        {
            AssignSeparators(d);
            double height = req.PayloadHeight + HeightOf(d.PayloadDecoupler);
            double width = req.PayloadWidth;
            int parts = req.PayloadParts + (d.PayloadDecoupler != null ? 1 : 0);
            double boosterTop = 0;
            for (int i = 0; i < d.Stages.Count; i++)
            {
                StagePlan s = d.Stages[i];
                double stage = TanksHeight(s.Tanks) + HeightOf(s.Engine);
                if (i > 0 && s.Decoupler != null) stage += HeightOf(s.Decoupler);
                height += stage;
                parts += TankParts(s.Tanks) + s.EngineCount + (i > 0 && s.Decoupler != null ? 1 : 0);
                double dia = CraftBuilder.DiameterOf(s.Size);
                width = Math.Max(width, dia);
                BoosterPlan b = s.Boosters;
                if (b != null && b.Count > 0)
                {
                    double bh = b.Solid ? HeightOf(b.Part) : TanksHeight(b.Tanks) + HeightOf(b.Part);
                    string bs = b.Solid || b.Tanks.Count == 0 ? SizeOf(b.Part) : SizeOf(b.Tanks[0].Tank);
                    boosterTop = Math.Max(boosterTop, bh);   // les boosters sont alignés sur le bas du premier étage
                    width = Math.Max(width, dia + 2 * CraftBuilder.DiameterOf(bs) + 0.2);
                    parts += b.Count * ((b.Solid ? 0 : TankParts(b.Tanks)) + 1 + (b.Decoupler != null ? 1 : 0) + b.SeparatorsEach + (b.Crossfeed ? 1 : 0));
                }
            }
            d.HeightM = Math.Max(height, boosterTop);
            d.WidthM = width;
            d.PartCount = parts;
        }

        static string Metres(double m)
        {
            return m.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " m";
        }

        /// <summary>Ce qui empêche de construire ou de lancer ce lanceur avec les bâtiments actuels, ou null.</summary>
        string SiteProblem(LauncherDesign d)
        {
            Measure(d);
            if (req.MaxHeight > 0 && d.HeightM > req.MaxHeight)
                return string.Format("trop haut : {0} pour {1} autorisés (améliore le pas de tir ou allège la charge utile)", Metres(d.HeightM), Metres(req.MaxHeight));
            if (req.MaxWidth > 0 && d.WidthM > req.MaxWidth)
                return string.Format("trop large : {0} pour {1} autorisés", Metres(d.WidthM), Metres(req.MaxWidth));
            if (req.MaxParts > 0 && d.PartCount > req.MaxParts)
                return string.Format("trop de pièces : {0} pour {1} autorisées par le VAB", d.PartCount, req.MaxParts);
            if (req.MaxMass > 0 && d.TotalMass > req.MaxMass)
                return string.Format("trop lourd pour le pas de tir : {0:0.#} t pour {1:0.#} t autorisées", d.TotalMass, req.MaxMass);
            return null;
        }

        PartInfo Adapter(string mixture, string bottom, string top)
        {
            Mixture mix;
            if (!mixtures.TryGetValue(mixture, out mix)) return null;
            PartInfo best = null;
            foreach (PartInfo a in mix.Adapters)
                if (HasSize(a, bottom) && HasSize(a, top) && (best == null || Propellant(a) > Propellant(best))) best = a;
            return best;
        }

        static void AddTank(List<TankUse> list, PartInfo tank)
        {
            foreach (TankUse t in list) if (t.Tank == tank) { t.Count++; return; }
            list.Add(new TankUse { Tank = tank, Count = 1 });
        }

        /// <summary>Choisit des réservoirs pour au moins `fuel` tonnes d'ergols : les plus gros d'abord, puis le plus petit qui suffit.</summary>
        /// <summary>Masse « inutile » d'un remplissage : masse à vide, ergols en trop ou manquants, et un peu par pièce.</summary>
        static double FillScore(double fuel, double got, double dry, int parts)
        {
            double extra = got - fuel;
            return dry + (extra >= 0 ? 0.5 * extra : 3 * -extra) + 0.02 * fuel * parts;
        }

        /// <param name="uniform">Un seul modèle de réservoir (boosters) : pas de réservoir d'une autre famille pour le reste.</param>
        List<TankUse> Fill(double fuel, string mixture, string size, out double actualFuel, out double dry, bool uniform = false)
        {
            List<TankUse> used = new List<TankUse>();
            actualFuel = 0;
            dry = 0;
            double bestRatio = BestRatio(TanksFor(mixture, size));
            List<PartInfo> tanks = TanksFor(mixture, size).FindAll(t => t.DryMassT / Propellant(t) <= bestRatio * TankRatioSlack);
            if (tanks.Count == 0 || fuel <= 0) return used;
            // À capacité égale, le réservoir le plus léger à vide.
            tanks.Sort((a, b) =>
            {
                int c = Propellant(b).CompareTo(Propellant(a));
                return c != 0 ? c : (a.DryMassT / Propellant(a)).CompareTo(b.DryMassT / Propellant(b));
            });

            // Un réservoir principal répété, plus au besoin un seul réservoir pour le reste :
            // peu de pièces, un empilement régulier, le moins possible d'ergols en trop.
            double bestScore = double.MaxValue;
            PartInfo bestMain = null, bestRest = null;
            int bestCount = 0;
            foreach (PartInfo main in tanks)
            {
                double p = Propellant(main);
                int whole = (int)Math.Floor(fuel / p + 1e-9);
                if (whole > 200) continue;
                for (int n = Math.Max(whole, 1); n <= whole + 1; n++)
                {
                    double rest = fuel - n * p;
                    PartInfo cover = null;
                    if (rest > 0.02 * fuel)
                    {
                        if (uniform) continue;
                        // Le plus petit réservoir qui couvre le reste.
                        foreach (PartInfo t in tanks) if (Propellant(t) >= rest - 1e-9) cover = t;
                        if (cover == null) continue;
                    }
                    double got = n * p + (cover != null ? Propellant(cover) : 0);
                    double d = n * main.DryMassT + (cover != null ? cover.DryMassT : 0);
                    int parts = n + (cover != null ? 1 : 0);
                    double score = FillScore(fuel, got, d, parts);
                    if (score < bestScore - 1e-9)
                    {
                        bestScore = score;
                        bestMain = main;
                        bestCount = n;
                        bestRest = cover;
                    }
                }
            }
            if (bestMain == null) return used;
            for (int i = 0; i < bestCount; i++) AddTank(used, bestMain);
            if (bestRest != null) AddTank(used, bestRest);
            actualFuel = bestCount * Propellant(bestMain) + (bestRest != null ? Propellant(bestRest) : 0);
            dry = bestCount * bestMain.DryMassT + (bestRest != null ? bestRest.DryMassT : 0);
            return used;
        }
    }
}
