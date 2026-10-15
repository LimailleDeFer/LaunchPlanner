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
    }

    /// <summary>
    /// Cherche, pour chaque variante (SSTO, deux étages, boosters à poudre, boosters liquides, asperges),
    /// la combinaison de pièces débloquées la plus légère (ou la moins chère) qui fournit le Δv demandé.
    /// Les quantités d'ergols sont d'abord continues, puis converties en vrais réservoirs.
    /// </summary>
    public class LauncherDesigner
    {
        const double G0 = PartInfo.G0;
        const int MaxEnginesPerStage = 4;    // étages supérieurs
        const int MaxLowerEngines = 7;       // premier étage (grappe de moteurs)
        const double MaxTankRatio = 0.2;     // masse à vide / masse d'ergols (≈ 0,125 pour les réservoirs du jeu de base)
        const double TankRatioSlack = 1.5;  // tolérance par rapport au meilleur réservoir lors du choix des pièces

        class Mixture
        {
            public string Key;
            public List<PartInfo> Tanks = new List<PartInfo>();
        }

        readonly DesignRequest req;
        readonly Dictionary<string, Mixture> mixtures = new Dictionary<string, Mixture>();
        readonly List<PartInfo> lowerEngines = new List<PartInfo>();
        readonly List<PartInfo> upperEngines = new List<PartInfo>();
        readonly List<PartInfo> solidBoosters = new List<PartInfo>();
        readonly List<PartInfo> decouplers = new List<PartInfo>();
        readonly Dictionary<PartInfo, string> engineMixture = new Dictionary<PartInfo, string>();

        public LauncherDesigner(DesignRequest request)
        {
            req = request;
            Index();
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
                    if (Propellant(p) <= 0 || p.DryMassT / Propellant(p) > MaxTankRatio) continue;
                    // Les adaptateurs (plusieurs diamètres) ne s'empilent pas en série : on ne garde que les réservoirs d'un seul diamètre.
                    if (StackSizes(p) != 1) continue;
                    List<string> names = new List<string>();
                    foreach (ResourceAmount r in p.Resources)
                        if (Propellants.Contains(r.Name) && r.Amount > 0) names.Add(r.Name);
                    if (names.Count == 0) continue;
                    string key = MixtureKey(names);
                    Mixture mix;
                    if (!mixtures.TryGetValue(key, out mix)) mixtures[key] = mix = new Mixture { Key = key };
                    mix.Tanks.Add(p);
                }
                else if (p.Kind == PartKind.Decoupler) decouplers.Add(p);
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

        double ComputeTankRatio(string mixture, string size)
        {
            double best = double.MaxValue;
            foreach (PartInfo t in TanksFor(mixture, size)) best = Math.Min(best, t.DryMassT / Propellant(t));
            return best == double.MaxValue ? 0.125 : best;
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
            foreach (PartInfo t in TanksFor(mixture, size))
            {
                double r = t.DryMassT / Propellant(t);
                if (r < ratio) { ratio = r; best = t.Cost / Propellant(t); }
            }
            return best == double.MaxValue ? 0 : best;
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

        PartInfo RadialDecoupler()
        {
            PartInfo best = null;
            foreach (PartInfo d in decouplers)
                if (SizeOf(d).Length == 0 && (best == null || d.DryMassT < best.DryMassT)) best = d;
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
            // Un premier étage qui ne fournit presque rien n'est qu'un SSTO déguisé.
            if (d.Stages.Count > 1 && d.Stages[0].DeltaV < MinFirstStageShare * req.TargetDeltaV)
                return "premier étage trop petit";
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

        double Score(LauncherDesign d)
        {
            return req.MinimizeCost ? d.Cost : d.TotalMass;
        }

        // ---------- Construction des étages ----------

        StagePlan MakeStage(PartInfo engine, int count, double fuel, bool withDecoupler)
        {
            StagePlan s = new StagePlan
            {
                Engine = engine,
                EngineCount = count,
                Mixture = engineMixture[engine],
                Size = SizeOf(engine),
                Fuel = fuel,
            };
            s.TankDry = fuel * TankRatio(s.Mixture, s.Size);
            if (withDecoupler)
            {
                s.Decoupler = StackDecoupler(s.Size);
                s.DecouplerMass = s.Decoupler != null ? s.Decoupler.DryMassT : 0.05;
            }
            return s;
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

        LauncherDesign Better(LauncherDesign best, LauncherDesign candidate)
        {
            if (!Constraints(candidate)) return best;
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
            List<LauncherDesign> result = new List<LauncherDesign>();
            if (req.TargetDeltaV <= 0) return result;
            List<Upper> front = UpperFront();
            Trace("front " + front.Count);

            pool.Clear(); Ssto();
            result.Add(Finish("SSTO", "Un seul étage, sans largage."));
            pool.Clear(); TwoStages(front, null);
            result.Add(Finish("Deux étages", "Deux étages empilés, sans boosters."));
            pool.Clear(); TwoStages(front, "solid");
            result.Add(Finish("Boosters à poudre", "Deux étages et des propulseurs à poudre largués en vol."));
            pool.Clear(); TwoStages(front, "liquid");
            result.Add(Finish("Boosters liquides", "Deux étages et des boosters liquides largués ensemble."));
            pool.Clear(); TwoStages(front, "asparagus");
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
                        for (int c = 2; c <= 8; c += 2)
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

            for (int i = d.Stages.Count - 1; i >= 1; i--) Quantize(d.Stages[i]);
            StagePlan first = d.Stages[0];
            SolveFirstStage(d);
            double wanted = first.Fuel;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                SetFuel(first, wanted);
                Quantize(first);
                double dv = Simulate(d);
                if (dv >= req.TargetDeltaV) break;
                wanted = Math.Max(wanted * 1.03, first.Fuel * (1 + (req.TargetDeltaV - dv) / req.TargetDeltaV));
            }

            d.Problem = "";
            if (Simulate(d) < 0) { d.Valid = false; return d; }
            twrMargin = 1;
            string problem = ConstraintProblem(d);
            twrMargin = 1.04;
            if (d.DeltaV < req.TargetDeltaV - 1) problem = string.Format("Δv insuffisant une fois les réservoirs arrondis ({0:0} m/s)", d.DeltaV);
            d.Valid = problem == null;
            if (!d.Valid) d.Problem = problem;
            foreach (StagePlan s in d.Stages)
            {
                if (s.Tanks.Count > 0 && !HasSize(s.Tanks[0].Tank, s.Size))
                    d.Notes.Add("Pas de réservoir " + s.Size + " pour " + s.Engine.Title + " : un adaptateur sera nécessaire.");
                if (s.Decoupler == null && s != first)
                    d.Notes.Add("Aucun découpleur débloqué à la bonne taille.");
            }
            return d;
        }

        string Unavailable(string variant)
        {
            if (variant == "Boosters à poudre" && solidBoosters.Count == 0) return "Aucun propulseur à poudre débloqué.";
            if (lowerEngines.Count == 0) return "Aucun moteur utilisable au décollage n'est débloqué.";
            return "Aucune combinaison de pièces débloquées n'atteint le Δv demandé avec un TWR suffisant.";
        }

        void Quantize(StagePlan s)
        {
            s.Tanks = Fill(s.Fuel, s.Mixture, s.Size, out s.Fuel, out s.TankDry);
            BoosterPlan b = s.Boosters;
            if (b != null && !b.Solid)
                b.Tanks = Fill(b.Fuel, b.Mixture, SizeOf(b.Part), out b.Fuel, out b.TankDry);
        }

        static void AddTank(List<TankUse> list, PartInfo tank)
        {
            foreach (TankUse t in list) if (t.Tank == tank) { t.Count++; return; }
            list.Add(new TankUse { Tank = tank, Count = 1 });
        }

        /// <summary>Choisit des réservoirs pour au moins `fuel` tonnes d'ergols : les plus gros d'abord, puis le plus petit qui suffit.</summary>
        List<TankUse> Fill(double fuel, string mixture, string size, out double actualFuel, out double dry)
        {
            List<TankUse> used = new List<TankUse>();
            actualFuel = 0;
            dry = 0;
            double bestRatio = TankRatio(mixture, size);
            List<PartInfo> tanks = TanksFor(mixture, size).FindAll(t => t.DryMassT / Propellant(t) <= bestRatio * TankRatioSlack);
            if (tanks.Count == 0 || fuel <= 0) return used;
            // À capacité égale, le réservoir le plus léger à vide.
            tanks.Sort((a, b) =>
            {
                int c = Propellant(b).CompareTo(Propellant(a));
                return c != 0 ? c : (a.DryMassT / Propellant(a)).CompareTo(b.DryMassT / Propellant(b));
            });

            double remaining = fuel;
            for (int guard = 0; guard < 200 && remaining > 1e-6; guard++)
            {
                PartInfo pick = null;
                foreach (PartInfo t in tanks) if (Propellant(t) <= remaining + 1e-9) { pick = t; break; }
                if (pick == null)
                {
                    // Aucun ne tient : le plus petit qui couvre le reste.
                    foreach (PartInfo t in tanks) if (Propellant(t) >= remaining) pick = t;
                    if (pick == null) pick = tanks[tanks.Count - 1];
                }
                AddTank(used, pick);
                actualFuel += Propellant(pick);
                dry += pick.DryMassT;
                remaining -= Propellant(pick);
            }
            return used;
        }
    }
}
