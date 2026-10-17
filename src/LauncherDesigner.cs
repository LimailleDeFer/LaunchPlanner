using System;
using System.Collections.Generic;

namespace LaunchPlanner
{
    public class TankUse
    {
        public PartInfo Tank;
        public int Count;
    }

    /// <summary>Boosters mounted around the first stage.</summary>
    public class BoosterPlan
    {
        public int Count;
        public bool Solid;
        public bool Crossfeed;          // asparagus: the boosters feed the core and are dropped 2 by 2
        public PartInfo Part;           // solid motor, or engine of a liquid booster
        public int EnginesEach = 1;
        public double Share = 1;        // liquid booster propellant relative to burning as long as the core (shorter boosters below 1)     // liquid booster: engines under its tank (a cluster, like Soyuz or Falcon Heavy)
        public PartInfo Coupler;        // multi-coupler carrying that cluster, or null (engines attached in a ring)
        public double CouplerMass;
        public string Mixture = "";
        public List<TankUse> Tanks = new List<TankUse>();
        public double Fuel;             // propellant per booster (t)
        public double TankDry;          // dry mass of a booster's tanks (t)
        public PartInfo Decoupler;      // radial decoupler
        public double DecouplerMass;
        public PartInfo Separator;      // separation motor (Sepratron) that pushes the dropped booster away
        public int SeparatorsEach;      // count per booster, based on its dry mass
        public PartInfo NoseCone;       // nose cone on top of the booster
        public List<PartInfo> NoseAdapters = new List<PartInfo>();   // adapters under the cone, bottom to top (no cone at the booster's size)
        public double NoseMass;

        public double DryEach
        {
            get { return Part.DryMassT * (Solid ? 1 : EnginesEach) + (Solid ? 0 : TankDry + CouplerMass) + DecouplerMass + NoseMass; }
        }

        public double FuelEach
        {
            // A liquid booster's engine may carry propellant of its own (engine and tank in one part).
            get { return Solid ? Part.WetMassT - Part.DryMassT : Fuel + EnginesEach * Math.Max(0, Part.WetMassT - Part.DryMassT); }
        }
    }

    /// <summary>
    /// Attitude control of a stage: reaction wheels and a monopropellant tank stacked between two of its tanks,
    /// near its center of mass, and RCS thrusters in two rings (top and bottom of its tanks).
    /// </summary>
    public class AttitudePlan
    {
        public PartInfo Wheel;
        public int Wheels;
        public int Joint;               // number of tank parts above the stacked group (0 = above the first tank)
        public PartInfo MonoTank;       // monopropellant for the RCS, stacked with the wheels, or radial
        public int MonoTanks;
        public bool MonoRadial;         // radial tanks at the height of the center of mass, one per ring position
        public PartInfo Rcs;
        public int RcsPerRing;
        public int Rings;
        public double RingAngle;        // heading of the first thruster of a ring (between the boosters)
        public double TorqueKNm;        // wheels
        public double NeededTorqueKNm;
        public double MonoT;            // monopropellant carried (t)

        public double Mass
        {
            get
            {
                return (Wheel != null ? Wheels * Wheel.DryMassT : 0) + (MonoTank != null ? MonoTanks * MonoTank.WetMassT : 0)
                    + (Rcs != null ? Rings * RcsPerRing * Rcs.DryMassT : 0);
            }
        }

        public double Cost
        {
            get
            {
                return (Wheel != null ? Wheels * Wheel.Cost : 0) + (MonoTank != null ? MonoTanks * MonoTank.Cost : 0)
                    + (Rcs != null ? Rings * RcsPerRing * Rcs.Cost : 0);
            }
        }

        /// <summary>Height added to the stage's stack.</summary>
        public double StackHeight;

        public int Parts
        {
            get { return (Wheel != null ? Wheels : 0) + (MonoTank != null ? MonoTanks : 0) + (Rcs != null ? Rings * RcsPerRing : 0); }
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
        public PartInfo Decoupler;      // decoupler under the stage (none for the first stage)
        public double DecouplerMass;
        public BoosterPlan Boosters;
        public PartInfo Coupler;        // multi-coupler under the tank, carrying the engine cluster
        public double CouplerMass;
        public AttitudePlan Attitude;   // reaction wheels and RCS, chosen once the tanks are real
        // Lander: tanks attached around the central tank, and the landing legs
        public PartInfo RadialTank;
        public int RadialCount;
        public int LegCount;
        public double HeightM;          // lander: height from the top of its tanks to the bottom of its engine
        public double WidthM;           // lander: central tank and the radial tanks around it
        // Lander in an atmosphere: parachutes (kept) and heat shield (dropped before the landing burn)
        public PartInfo Chute, Drogue, HeatShield;
        public int Chutes, Drogues;
        public double TouchdownSpeed;   // m/s under the main chutes alone

        public double ChuteMass
        {
            get { return (Chute != null ? Chutes * Chute.DryMassT : 0) + (Drogue != null ? Drogues * Drogue.DryMassT : 0); }
        }

        // Simulation results
        public double DeltaV;
        public double StartDeltaV;      // delta-v already provided by the lower stages at ignition
        public double CoreTwr;          // TWR of the core alone, once the boosters are dropped
        public double CoreStartDeltaV;  // cumulative delta-v at booster drop
        public double Cost;
        public double Twr;
        public double MassStart;

        public double DryMass
        {
            get { return EngineCount * Engine.DryMassT + TankDry + DecouplerMass + CouplerMass + (Attitude != null ? Attitude.Mass : 0) + ChuteMass; }
        }

        /// <summary>Propellant carried by the engines themselves (engine and tank in one part, such as some Tantares engines).</summary>
        public double EngineFuel
        {
            get { return EngineCount * Math.Max(0, Engine.WetMassT - Engine.DryMassT); }
        }

        public double TotalMass
        {
            get
            {
                double m = DryMass + Fuel + EngineFuel + (HeatShield != null ? HeatShield.WetMassT : 0);
                if (Boosters != null) m += Boosters.Count * (Boosters.DryEach + Boosters.FuelEach);
                return m;
            }
        }
    }

    public class LauncherDesign
    {
        public string Variant;
        public string Description;
        public List<StagePlan> Stages = new List<StagePlan>();   // from the first stage (bottom) to the last (top)
        public bool Valid;
        public string Problem = "";
        public double TotalMass;      // launcher + payload
        public double LauncherMass;
        public double Cost;
        public double DeltaV;
        public double LiftoffTwr;
        public List<string> Notes = new List<string>();
        public PartInfo PayloadDecoupler;   // decoupler between the payload and the last stage

        // Lander stage, under the payload, when it cannot land by itself
        public StagePlan Lander;
        public PartInfo LanderLegs;
        public int LanderLegCount;

        // Estimated size, payload included
        public double HeightM;
        public double BoosterExcessM;
        public double HeightOfBoostersM;   // how much the boosters rise above the core's first stage (0 if they are shorter)
        public double WidthM;
        public int PartCount;
    }

    public class DesignRequest
    {
        public List<PartInfo> Parts;
        public double PayloadMass;      // t
        public double TargetDeltaV;     // m/s the launcher must provide
        public double HomeGravity = 9.81;
        public bool HomeAtmosphere = true;
        public double MinLiftoffTwr = 1.3;
        public double MinUpperTwr = 0.5;
        public double AscentDeltaV = 3400; // delta-v of ascent to orbit from the ground: stages lit earlier need thrust
        public bool MinimizeCost;

        // VAB and launch pad limits (0 = no limit, e.g. in sandbox mode)
        public double MaxHeight;        // m
        public double MaxWidth;         // m
        public int MaxParts;
        public double MaxMass;          // t, at liftoff
        public double PayloadHeight;    // m
        public double PayloadWidth;     // m
        public int PayloadParts;

        // Lander stage (0 = the payload lands by itself)
        public double LanderDeltaV;     // m/s the lander must provide
        public double LanderGravity;    // m/s², gravity at the destination's surface
        public double LanderTwr = 2;    // minimum TWR against that gravity
        public string LanderBody = "";
        public List<PartInfo> Legs = new List<PartInfo>();   // unlocked landing legs
        public double LanderPressureAtm;   // surface pressure at the destination (0: no atmosphere)
        public double LanderEntrySpeed;    // m/s, low-orbit speed at the destination: the speed of the entry
        public List<PartInfo> Parachutes = new List<PartInfo>();
        public List<PartInfo> HeatShields = new List<PartInfo>();

        public List<PartInfo> NoseCones = new List<PartInfo>();   // unlocked nose cones (top of boosters)
        public List<PartInfo> Adapters = new List<PartInfo>();    // unlocked structural adapters (narrow top), to reach a cone's size
        public List<PartInfo> Couplers = new List<PartInfo>();    // unlocked multi-couplers (engine clusters)
        public List<PartInfo> ReactionWheels = new List<PartInfo>();   // unlocked inline reaction wheels (TorqueKNm, two stack nodes)
        public List<PartInfo> RcsThrusters = new List<PartInfo>();     // unlocked monopropellant RCS thrusters (surface attach)
        public double PayloadTorqueKNm;  // reaction wheels already in the payload (probe cores, pods...)
        public double PayloadRcsKN;      // RCS already in the payload: thrust of all its nozzles together
        public string PayloadBottomSize = "";                     // profile of the payload's bottom node ("" = unknown)
    }

    /// <summary>
    /// Searches, for each variant (SSTO, two stages, solid boosters, liquid boosters, asparagus),
    /// for the lightest (or cheapest) combination of unlocked parts that provides the requested delta-v.
    /// Propellant quantities are first continuous, then converted into real tanks.
    /// </summary>
    public class LauncherDesigner
    {
        const double G0 = PartInfo.G0;
        const int MaxEnginesPerStage = 2;    // upper stages: one vacuum engine (two at most, like Centaur)
        const int MaxLowerEngines = 7;       // first stage (engine cluster)
        const int MaxBoosterEngines = 7;   // liquid booster cluster: as many as fit under its tank
        // Liquid boosters burning as long as the core, or shorter ones that are dropped earlier (they must stay below its top).
        static readonly double[] BoosterShares = { 1.0 };   // then adjusted per design in Finish when the boosters stand too tall
        static readonly int[] BoosterCounts = { 2, 3, 4, 6, 8 };
        static readonly int[] AsparagusCounts = { 2, 4, 6, 8 };   // dropped in pairs     // liquid booster (Soyuz: 4 chambers per booster)
        const double MaxTankRatio = 0.2;     // dry mass / propellant mass (about 0.125 for stock tanks)
        const double TankRatioSlack = 1.5;  // tolerance relative to the best tank when choosing parts

        class Mixture
        {
            public string Key;
            public List<PartInfo> Tanks = new List<PartInfo>();
            public List<PartInfo> Adapters = new List<PartInfo>();   // two-diameter tanks (top of a wider stage)
            public List<string> Sizes = new List<string>();          // available diameters, from smallest to largest
        }

        const int MaxTankParts = 6;   // beyond that, move to the next diameter up: wide base, narrowing stages

        readonly DesignRequest req;
        double topMinDiameter;   // narrowest top stage: the lander's central tank
        readonly Dictionary<string, Mixture> mixtures = new Dictionary<string, Mixture>();
        readonly List<PartInfo> lowerEngines = new List<PartInfo>();
        readonly List<PartInfo> upperEngines = new List<PartInfo>();
        readonly List<PartInfo> solidBoosters = new List<PartInfo>();
        readonly List<PartInfo> decouplers = new List<PartInfo>();
        readonly Dictionary<PartInfo, string> engineMixture = new Dictionary<PartInfo, string>();

        bool heightsKnown;   // the inventory comes from the game: each part's stack height is known

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
        /// Unlocked separation motor: small solid motor to attach on the surface, burning for a few seconds.
        /// The Sepratron I is preferred, then anything with "sep" in its name, then the lightest.
        /// </summary>
        public static PartInfo PickSeparator(List<PartInfo> parts)
        {
            PartInfo best = null;
            int bestRank = -1;
            foreach (PartInfo p in parts)
            {
                if (!p.Unlocked || p.Kind != PartKind.Booster || (p.Sizes ?? "").Trim() != "srf") continue;
                // A real separation motor is tiny: we discard separation "packs" as long as a booster
                // and parts removed from their mod.
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
        /// Number of separation motors per booster: enough to push the empty booster away at about 12 m/s,
        /// at least one at the top and one at the bottom, always an even number, at most 8.
        /// </summary>
        public static int SeparatorCount(PartInfo sep, double boosterDryT)
        {
            if (sep == null) return 0;
            double impulse = sep.ThrustVacKN * sep.BurnTimeS * 0.94;   // kN·s, tilted 20° rearward
            int n = impulse > 0 ? (int)Math.Ceiling(boosterDryT * 12 / impulse) : 2;
            n = Math.Max(2, n + (n & 1));
            return Math.Min(8, n);
        }

        void AssignBoosterParts(LauncherDesign d)
        {
            BoosterPlan b = d.Stages.Count > 0 ? d.Stages[0].Boosters : null;
            if (b == null || b.Count == 0) return;
            b.Separator = separator;
            b.SeparatorsEach = SeparatorCount(separator, b.Part.DryMassT * (b.Solid ? 1 : b.EnginesEach) + (b.Solid ? 0 : b.TankDry));
            PartInfo top = b.Solid ? b.Part : b.Tanks.Count > 0 ? b.Tanks[0].Tank : null;
            b.NoseCone = null;
            b.NoseAdapters = new List<PartInfo>();
            if (top != null && !top.NoseTop && (!heightsKnown || top.HeightM > 0))
            {
                List<PartInfo> chain = NoseChain(TopSize(top));
                if (chain != null)
                {
                    b.NoseCone = chain[chain.Count - 1];
                    b.NoseAdapters = chain.GetRange(0, chain.Count - 1);
                }
            }
            b.NoseMass = 0;
            if (b.NoseCone != null) b.NoseMass += b.NoseCone.DryMassT;
            foreach (PartInfo a in b.NoseAdapters) b.NoseMass += a.DryMassT;
        }

        /// <summary>Profile of a part's top: the narrower one for an adapter tank.</summary>
        static string TopSize(PartInfo p)
        {
            string best = SizeOf(p);
            foreach (string raw in (p.Sizes ?? "").Split(new[] { ',' }))
            {
                string s = raw.Trim();
                if (s.StartsWith("size") && CraftBuilder.DiameterOf(s) < CraftBuilder.DiameterOf(best)) best = s;
            }
            return best;
        }

        /// <summary>
        /// Lightest way to cap a top of this size: a cone of that size, or up to two structural adapters
        /// toward a narrower size that has one (e.g. 3.75 → 2.5 → 1.25 m), then that cone. Null if none.
        /// The adapters come first, bottom to top, and the cone last.
        /// </summary>
        List<PartInfo> NoseChain(string size)
        {
            List<PartInfo> best = null;
            double bestMass = double.MaxValue;
            PartInfo cone = NoseCone(size);
            if (cone != null) return new List<PartInfo> { cone };
            foreach (PartInfo a in req.Adapters)
            {
                if (a.AdapterBottom != size) continue;
                PartInfo c = NoseCone(a.AdapterTop);
                if (c != null && a.DryMassT + c.DryMassT < bestMass) { bestMass = a.DryMassT + c.DryMassT; best = new List<PartInfo> { a, c }; }
                foreach (PartInfo a2 in req.Adapters)
                {
                    if (a2.AdapterBottom != a.AdapterTop) continue;
                    PartInfo c2 = NoseCone(a2.AdapterTop);
                    double m = a.DryMassT + a2.DryMassT + (c2 != null ? c2.DryMassT : 0);
                    if (c2 != null && m < bestMass - 0.05) { bestMass = m; best = new List<PartInfo> { a, a2, c2 }; }
                }
            }
            return best;
        }

        /// <summary>
        /// Adapter tank (propellant of the booster) to put on top of a booster of this size, when no cone fits
        /// that size directly: its narrow top takes a cone, and its propellant counts. The fullest, or null.
        /// </summary>
        PartInfo FuelNoseAdapter(string mixture, string size, double fuel)
        {
            Mixture mix;
            if (NoseCone(size) != null || !mixtures.TryGetValue(mixture, out mix)) return null;
            PartInfo best = null;
            foreach (PartInfo a in mix.Adapters)
            {
                if (!HasSize(a, size) || TopSize(a) == size || Propellant(a) >= fuel) continue;
                if (NoseChain(TopSize(a)) == null) continue;
                if (best == null || Propellant(a) > Propellant(best)) best = a;
            }
            return best;
        }

        /// <summary>Nose cone sized for the top of the booster: the lightest, like those of real boosters.</summary>
        PartInfo NoseCone(string size)
        {
            PartInfo best = null;
            foreach (PartInfo c in req.NoseCones)
                if (HasSize(c, size) && (best == null || c.DryMassT < best.DryMassT)) best = c;
            return best;
        }

        /// <summary>
        /// Multi-coupler that carries exactly this cluster under a tank of this diameter: top node sized to the
        /// tank, bottom nodes spaced widely enough for the engines. The lightest if there are several.
        /// </summary>
        PartInfo Coupler(string size, PartInfo engine, int count)
        {
            if (count < 2) return null;
            double tankD = CraftBuilder.DiameterOf(size), engineD = CraftBuilder.DiameterOf(SizeOf(engine));
            PartInfo best = null;
            foreach (PartInfo c in req.Couplers)
            {
                if (c.StackOutputs != count || Math.Abs(c.TopDiameterM - tankD) > 0.01) continue;
                if (c.OutputSpacingM < 0.9 * engineD) continue;
                if (best == null || c.DryMassT < best.DryMassT) best = c;
            }
            return best;
        }

        // ---------- Part preparation ----------

        public static string SizeOf(PartInfo p)
        {
            foreach (string raw in (p.Sizes ?? "").Split(new[] { ',' }))
            {
                string s = raw.Trim();
                if (s.Length > 0 && s != "srf") return s;
            }
            return "";
        }

        static int StackSizes(PartInfo p)
        {
            int n = 0;
            foreach (string raw in (p.Sizes ?? "").Split(new[] { ',' }))
            {
                string s = raw.Trim();
                if (s.Length > 0 && s != "srf") n++;
            }
            return n;
        }

        static bool HasSize(PartInfo p, string size)
        {
            foreach (string raw in (p.Sizes ?? "").Split(new[] { ',' }))
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
                    // Only real tanks: discard parts whose fuel is just a side feature
                    // (capsules, compartments, skirts...), too heavy for the amount carried.
                    if (Propellant(p) <= 0 || p.DryMassT / Propellant(p) > MaxTankRatio * 1.3) continue;
                    // Fairings and single-stack-node parts: nothing can be attached underneath.
                    if (IsNoseCone(p) || (heightsKnown && p.HeightM <= 0)) continue;
                    // Model clearly narrower than its profile: it would spoil the launcher's look (wide stage on a thin stage).
                    if (p.DiameterM > 0 && p.DiameterM < 0.8 * CraftBuilder.DiameterOf(SizeOf(p))) continue;
                    // Adapters (several diameters) are not stacked in series: they only join two stages.
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
                // A solid motor that burns for less than 15 s (escape tower, separation motor) is not a booster.
                else if (p.Kind == PartKind.Booster && p.ThrustAslKN > 0 && p.IspVac > 0 && Propellant(p) > 0 && p.BurnTimeS >= 15)
                    solidBoosters.Add(p);
            }

            foreach (PartInfo p in req.Parts)
            {
                if (!p.Unlocked || p.Kind != PartKind.Engine || p.ThrustVacKN <= 0 || p.IspVac <= 0) continue;
                if (string.Equals((p.Title ?? "").Trim(), "deprecated", StringComparison.OrdinalIgnoreCase)) continue;   // retired by its mod
                if (SizeOf(p).Length == 0) continue;   // radial engines: not for a stacked stage
                string key = MixtureKey((p.Propellants ?? "").Split(new[] { '+' }));
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

        // Removes dominated engines: same propellants and size, but less thrust, less Isp and heavier.
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

        // Dry mass / propellant mass ratio of the best tank of this type and size.
        double TankRatio(string mixture, string size)
        {
            string key = mixture + "|" + size;
            double cached;
            if (ratioCache.TryGetValue(key, out cached)) return cached;
            return ratioCache[key] = ComputeTankRatio(mixture, size);
        }

        // Tanks usable with an engine of this size: same diameter or wider.
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
        /// Best dry mass / propellant ratio among the tanks that matter: a small, exceptionally light tank
        /// (a few hundred kilos) must not make us believe the whole stage will be as light.
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
        /// Many parts can detach (heat shields, capsules, parachutes, fairings...): we only keep
        /// those in the editor's "Coupling" category, or, with no known category, those named decoupler.
        /// </summary>
        static bool IsRealDecoupler(PartInfo p)
        {
            if (!string.IsNullOrEmpty(p.Category)) return p.Category == "Coupling";
            string t = (p.Title + " " + p.Name).ToLowerInvariant();
            return t.Contains("decoupl") || t.Contains("découpl") || t.Contains("separator") || t.Contains("séparateur") || t.Contains("interstage");
        }

        /// <summary>
        /// Decoupler under the payload: sized to its bottom node when known (not with a lander,
        /// where it goes under the lander), otherwise sized to the last stage.
        /// </summary>
        PartInfo PayloadDecoupler(string stageSize)
        {
            string size = req.PayloadBottomSize;
            if (string.IsNullOrEmpty(size) || req.LanderDeltaV > 0) return StackDecoupler(stageSize);
            foreach (PartInfo d in decouplers)
                if (HasSize(d, size)) return StackDecoupler(size);
            return StackDecoupler(stageSize);
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

        // The smallest radial decouplers give way under big boosters: we take the sturdiest one that stays light.
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
            return e.ThrustVacKN / (e.IspVac * G0);   // t/s, constant regardless of pressure
        }

        /// <summary>Computes delta-v, TWR and masses of each stage. Returns the total delta-v, or -1 if the configuration is inconsistent.</summary>
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
                double coreFuel = s.Fuel + s.EngineFuel;
                s.StartDeltaV = total;

                s.Twr = liftThrust / (s.MassStart * req.HomeGravity);
                s.CoreTwr = 0;
                BoosterPlan b = s.Boosters;
                if (b != null && b.Count > 0)
                {
                    int k = b.Solid ? 1 : b.EnginesEach;   // engines per booster
                    double mdotB = k * MassFlow(b.Part);
                    double thrB = mdotB * Isp(b.Part, atmo) * G0;
                    liftThrust += b.Count * k * (i == 0 && req.HomeAtmosphere ? b.Part.ThrustAslKN : b.Part.ThrustVacKN);
                    s.Twr = liftThrust / (s.MassStart * req.HomeGravity);
                    if (b.Crossfeed)
                    {
                        // Asparagus: all pairs feed the engines, the outer pair empties first.
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
                        // Independent boosters: they burn at the same time as the core and are then dropped.
                        double tB = b.FuelEach / mdotB;
                        if (mdotC * tB > coreFuel)
                        {
                            // The core shuts down first: the boosters finish alone, then the whole stage is dropped.
                            double tC = coreFuel / mdotC;
                            double m1 = mass - (mdotC + b.Count * mdotB) * tC;
                            dv += (thrC + b.Count * thrB) / (mdotC + b.Count * mdotB) * Math.Log(mass / m1);
                            double m2 = m1 - b.Count * (b.FuelEach - mdotB * tC);
                            dv += thrB / mdotB * Math.Log(m1 / m2);
                            double boosterThrust = b.Count * k * (atmo ? (b.Part.ThrustAslKN + b.Part.ThrustVacKN) / 2 : b.Part.ThrustVacKN);
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
                    // Core thrust between vacuum and sea level, mid-ascent.
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
                if (s.Attitude != null) cost += s.Attitude.Cost;
                BoosterPlan b = s.Boosters;
                if (b != null)
                {
                    double each = b.Part.Cost * (b.Solid ? 1 : b.EnginesEach) + (b.Decoupler != null ? b.Decoupler.Cost : 0)
                        + (b.Coupler != null ? b.Coupler.Cost : 0);
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

        /// <summary>The first constraint not met, or null.</summary>
        // During the search, a little more TWR is required: rounding to real tanks adds mass.
        double twrMargin = 1.04;

        string ConstraintProblem(LauncherDesign d)
        {
            if (d.Stages.Count == 0) return "no stage";
            if (d.Stages[0].Twr < req.MinLiftoffTwr * twrMargin) return string.Format("liftoff TWR too low ({0:0.00})", d.Stages[0].Twr);
            for (int i = 1; i < d.Stages.Count; i++)
                if (d.Stages[i].Twr < MinTwr(d.Stages[i].StartDeltaV) * twrMargin)
                    return string.Format("stage {0} TWR too low ({1:0.00})", i + 1, d.Stages[i].Twr);
            foreach (StagePlan s in d.Stages)
                if (s.Boosters != null && s.Boosters.Count > 0 && s.CoreTwr < MinTwr(s.CoreStartDeltaV) * twrMargin)
                    return string.Format("core TWR too low after booster drop ({0:0.00})", s.CoreTwr);
            // Mounting is possible: the cluster fits under the widest unlocked tank, the boosters fit around the core.
            foreach (StagePlan s in d.Stages)
            {
                if (s.EngineCount > 1 && !ClusterFits(s.Mixture, s.Engine, s.EngineCount))
                    return string.Format("{0} engines do not fit under the widest unlocked tank", s.EngineCount);
                BoosterPlan b = s.Boosters;
                if (b != null && b.Count > 0)
                {
                    double core = CraftBuilder.DiameterOf(LargestSize(s.Mixture, s.Size));
                    double each = CraftBuilder.DiameterOf(b.Solid || b.EnginesEach <= 1 ? SizeOf(b.Part) : ClusterSize(b.Mixture, b.Part, b.EnginesEach));
                    if (!b.Solid && b.EnginesEach > 1 && each > core + 0.01)
                        return string.Format("{0} engines do not fit under a booster no wider than the core", b.EnginesEach);
                    if (b.Count * each > Math.PI * (core + each) * 0.95)
                        return string.Format("{0} boosters do not fit around the core", b.Count);
                }
            }
            // A first stage that provides almost nothing is just a disguised SSTO.
            if (d.Stages.Count > 1 && d.Stages[0].DeltaV < MinFirstStageShare * req.TargetDeltaV)
                return "first stage too small";
            // An upper stage that adds almost nothing is not worth its decoupler (on real launchers, 30 to 50%).
            for (int i = 1; i < d.Stages.Count; i++)
                if (d.Stages[i].DeltaV < 0.15 * req.TargetDeltaV) return "upper stage too small";
            return null;
        }

        /// <summary>
        /// Minimum TWR of an upper stage: a stage lit mid-ascent must still fight gravity,
        /// a stage lit once in orbit can be sluggish.
        /// </summary>
        double MinTwr(double startDeltaV)
        {
            // From 0.9 mid-ascent down to the chosen minimum once orbital speed is reached, with no step.
            double x = (startDeltaV / req.AscentDeltaV - 0.5) / 0.5;
            if (!req.HomeAtmosphere) x = startDeltaV / (req.AscentDeltaV * 0.5);
            x = Math.Max(0, Math.Min(1, x));
            double high = Math.Max(0.9, req.MinUpperTwr);
            return high + (req.MinUpperTwr - high) * x;
        }

        /// <summary>
        /// Mass (or cost) to minimize. Each engine beyond one per stage costs 2%: a cluster only beats a single
        /// big engine if it is clearly more efficient.
        /// </summary>
        double Score(LauncherDesign d)
        {
            int extra = 0;
            foreach (StagePlan s in d.Stages) extra += Math.Max(0, s.EngineCount - 1);
            return (req.MinimizeCost ? d.Cost : d.TotalMass) * (1 + ClusterPenalty * extra);
        }

        const double ClusterPenalty = 0.02;

        // ---------- Stage construction ----------

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

        /// <summary>Smallest tank diameter under which the engine cluster fits.</summary>
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
                // A liquid booster burns like the core: its share of propellant follows its engine's flow.
                b.Fuel = b.Share * fuel * b.EnginesEach * MassFlow(b.Part) / Math.Max(1e-9, s.EngineCount * MassFlow(s.Engine));
                b.TankDry = b.Fuel * TankRatio(b.Mixture, SizeOf(b.Part));
            }
        }

        /// <summary>Finds the minimum first-stage propellant quantity that reaches the target delta-v.</summary>
        bool SolveFirstStage(LauncherDesign d, bool checkTwr = true)
        {
            StagePlan s = d.Stages[0];
            // Liftoff TWR drops as propellant is added: if it is already too low when empty, there is no point searching.
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

        // Possible upper stages (delta-v, mass); only the non-dominated ones are kept.
        List<Upper> UpperFront()
        {
            // One engine on the bottom node (Sebastien's rule): it fills the space above the decoupler, whereas a
            // ring of engines on the tank's side leaves a gap down to it. Two only if no single engine will do.
            List<Upper> front = UpperFront(1);
            return front.Count > 0 ? front : UpperFront(MaxEnginesPerStage);
        }

        List<Upper> UpperFront(int maxEngines)
        {
            List<Upper> all = new List<Upper>();
            double payload = Math.Max(0.1, req.PayloadMass);
            foreach (PartInfo e in upperEngines)
                for (int n = 1; n <= maxEngines; n++)
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

            // To keep computation time reasonable, the front is sampled.
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

        // Best candidates of the current variant: if rounding to real tanks makes the first one fail,
        // the next ones are tried.
        readonly List<LauncherDesign> pool = new List<LauncherDesign>();
        const int PoolSize = 12;

        // Rejection reasons during the search of the current variant, to explain a failure.
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
            Simulate(copy);   // recomputes masses, cost and delta-v on the copy
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
                        EnginesEach = b.EnginesEach, Share = b.Share, Coupler = b.Coupler, CouplerMass = b.CouplerMass,
                        Tanks = new List<TankUse>(b.Tanks), Fuel = b.Fuel, TankDry = b.TankDry,
                        Decoupler = b.Decoupler, DecouplerMass = b.DecouplerMass,
                        Separator = b.Separator, SeparatorsEach = b.SeparatorsEach, NoseCone = b.NoseCone, NoseMass = b.NoseMass,
                        NoseAdapters = new List<PartInfo>(b.NoseAdapters),
                    };
                }
                c.Stages.Add(n);
            }
            return c;
        }

        // ---------- Variants ----------

        public string Stats
        {
            get
            {
                return string.Format("{0} liftoff engines, {1} upper-stage engines, {2} solid motors, {3} propellant types",
                    lowerEngines.Count, upperEngines.Count, solidBoosters.Count, mixtures.Count);
            }
        }

        public List<LauncherDesign> Run()
        {
            if (req.LanderDeltaV <= 0) return RunLauncher();

            PartInfo legs;
            string problem;
            StagePlan lander = DesignLander(out legs, out problem);
            // The engine is chosen before the RCS and parachutes are known: aim higher if they bring the TWR under the minimum.
            double wantedTwr = req.LanderTwr;
            for (int retry = 0; retry < 3 && lander != null && (lander.Twr < wantedTwr - 1e-3 || lander.DeltaV < req.LanderDeltaV - 1); retry++)
            {
                double asked = req.LanderTwr;
                req.LanderTwr *= Math.Max(wantedTwr / lander.Twr, 1) * 1.05;
                PartInfo l2;
                string p2;
                StagePlan again = DesignLander(out l2, out p2);
                req.LanderTwr = asked;
                if (again == null) break;
                lander = again; legs = l2;
            }
            if (lander == null)
            {
                List<LauncherDesign> none = new List<LauncherDesign>();
                foreach (string[] v in Variants)
                    none.Add(new LauncherDesign { Variant = v[0], Description = v[1], Problem = problem });
                return none;
            }
            int legCount = legs != null ? lander.LegCount : 0;
            double mass = req.PayloadMass, height = req.PayloadHeight, width = req.PayloadWidth;
            int parts = req.PayloadParts;
            // The launcher carries the payload and its lander.
            req.PayloadMass += lander.TotalMass + legCount * (legs != null ? legs.DryMassT : 0);
            req.PayloadHeight += lander.HeightM + HeightOf(lander.HeatShield);
            req.PayloadWidth = Math.Max(req.PayloadWidth, lander.WidthM + 1);
            req.PayloadParts += TankParts(lander.Tanks) + lander.RadialCount + lander.EngineCount + legCount
                + (lander.Attitude != null ? lander.Attitude.Parts : 0) + lander.Chutes + lander.Drogues + (lander.HeatShield != null ? 1 : 0);
            topMinDiameter = CraftBuilder.DiameterOf(lander.Size);
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
                topMinDiameter = 0;
            }
        }

        static readonly string[][] Variants =
        {
            new[] { "SSTO", "A single stage, no staging." },
            new[] { "Two stages", "Two stacked stages, no boosters." },
            new[] { "Solid boosters", "Two stages and solid motors dropped in flight." },
            new[] { "Liquid boosters", "Two stages and liquid boosters dropped together." },
            new[] { "Asparagus", "Liquid boosters dropped 2 by 2; each pair feeds the rest of the launcher before being dropped." },
        };

        // ---------- Lander stage ----------

        const int LanderLegs = 4;
        const int MaxLanderEngines = 4;

        /// <summary>
        /// Landing legs: the lightest one that supports the landed mass (about 0.4% of that mass per leg,
        /// like the stock legs), otherwise the sturdiest.
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
        /// Stage that lands the payload and handles what it cannot do alone (descent, then ascent and return
        /// if the mission includes them): one or a few vacuum engines, enough thrust against the destination's gravity,
        /// storable propellants (no liquid hydrogen, which boils off during the trip).
        /// </summary>
        StagePlan DesignLander(out PartInfo legs, out string problem)
        {
            legs = null;
            problem = "No unlocked engine can land the payload on " + req.LanderBody + ".";
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
                if (tooWeak) problem = string.Format("No unlocked engine has enough thrust to land the payload on {0} (TWR {1:0.#} required).",
                    req.LanderBody, req.LanderTwr);
                return null;
            }

            if (!LayoutLander(best, legs))
            {
                // No layout reaches the delta-v with one or two central tanks: a stack, as squat as possible.
                double wanted = best.Fuel;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    double minD = LanderMinDiameter(best);
                    best.Size = SquatSize(wanted, best.Mixture, minD) ??
                        ChooseSize(wanted, best.Mixture, best.Engine, best.EngineCount, minD, double.MaxValue);
                    double got, dry;
                    best.Tanks = Fill(wanted, best.Mixture, best.Size, out got, out dry);
                    best.Fuel = got;
                    best.TankDry = dry;
                    if (LanderDeltaV(best, legs) >= req.LanderDeltaV - 1) break;
                    wanted *= 1.05;
                }
                best.RadialTank = null;
                best.RadialCount = 0;
                best.LegCount = LanderLegs;
                best.HeightM = TanksHeight(best.Tanks) + HeightOf(best.Engine);
                best.WidthM = CraftBuilder.DiameterOf(best.Size);
            }
            // The RCS, its monopropellant and the parachutes weigh too: lay out again with them while the delta-v or the TWR falls short.
            for (int pass = 0; pass < 3; pass++)
            {
                best.Attitude = PlanLanderAttitude(best, legs);
                PlanReentry(best, legs);
                if (LanderDeltaV(best, legs) >= req.LanderDeltaV - 1 && LanderTwrNow(best, legs) >= req.LanderTwr) break;
                if (!LayoutLander(best, legs)) break;
            }
            best.DeltaV = LanderDeltaV(best, legs);
            // At ignition for the landing: the heat shield is already gone.
            best.MassStart = req.PayloadMass + best.TotalMass - (best.HeatShield != null ? best.HeatShield.WetMassT : 0)
                + best.LegCount * (legs != null ? legs.DryMassT : 0);
            best.Twr = best.EngineCount * best.Engine.ThrustVacKN / (best.MassStart * req.LanderGravity);
            best.Cost = best.EngineCount * best.Engine.Cost + LanderTankCost(best) + best.LegCount * (legs != null ? legs.Cost : 0)
                + (best.Attitude != null ? best.Attitude.Cost : 0) + (best.Chute != null ? best.Chutes * best.Chute.Cost : 0)
                + (best.Drogue != null ? best.Drogues * best.Drogue.Cost : 0) + (best.HeatShield != null ? best.HeatShield.Cost : 0);
            if (best.Tanks.Count == 0) { problem = "No unlocked tank for the lander."; return null; }
            return best;
        }

        // Lander layout: a short central tank (a service tank with storage when there is one), with if needed
        // narrow tanks attached around it rather than a tower of tanks.
        const double LanderAspectWeight = 0.4;   // score penalty per unit of height / width
        const double LanderPartWeight = 0.02;    // per radial tank
        const double StorageBonus = 0.9;         // central tank with a cargo bay or inventory slots

        /// <summary>Narrowest central tank: wide enough for the engines and for the payload's bottom.</summary>
        double LanderMinDiameter(StagePlan s)
        {
            double payload = !string.IsNullOrEmpty(req.PayloadBottomSize) ? CraftBuilder.DiameterOf(req.PayloadBottomSize) : 0.5 * req.PayloadWidth;
            return Math.Max(CraftBuilder.DiameterOf(ClusterSize(s.Mixture, s.Engine, s.EngineCount)), payload);
        }

        /// <summary>
        /// Chooses the lander's tanks among all layouts that give the delta-v and the TWR: one or two central tanks,
        /// alone or with 2 to 8 narrower tanks attached around them, no taller than the central tanks and the engine.
        /// The lightest (or cheapest) wins, squat layouts being favored. False if none fits.
        /// </summary>
        bool LayoutLander(StagePlan s, PartInfo legs)
        {
            Mixture mix;
            if (!mixtures.TryGetValue(s.Mixture, out mix)) return false;
            double minD = LanderMinDiameter(s);
            // As wide as the payload, more if the engines require it; never wider than the launch pad allows.
            double maxD = req.PayloadWidth > 0 ? Math.Max(minD, req.PayloadWidth) : double.MaxValue;   // unknown payload: any width
            if (req.MaxWidth > 0) maxD = Math.Min(maxD, req.MaxWidth);
            double engineH = HeightOf(s.Engine);
            double legMass = legs != null ? legs.DryMassT : 0;
            double attitude = s.Attitude != null ? s.Attitude.StackHeight : 0;

            double shield = s.HeatShield != null ? s.HeatShield.WetMassT : 0;   // dropped before the engine fires
            double oldFuel = s.Fuel, oldDry = s.TankDry;
            int oldLegs = s.LegCount;
            double bestScore = double.MaxValue;
            PartInfo bestCore = null, bestRadial = null;
            int bestCores = 0, bestCount = 0;
            foreach (PartInfo core in mix.Tanks)
            {
                double D = CraftBuilder.DiameterOf(SizeOf(core));
                if (core.NoseTop || D < minD - 0.01 || D > maxD + 0.01) continue;
                for (int cores = 1; cores <= 2; cores++)
                {
                    double coreH = cores * HeightOf(core);
                    // Radial tanks only when the central tanks alone fall short, and no more of them than needed.
                    s.Fuel = cores * Propellant(core);
                    s.TankDry = cores * core.DryMassT;
                    s.LegCount = LanderLegs;
                    bool alone = LanderDeltaV(s, legs) >= req.LanderDeltaV - 1;
                    List<PartInfo> radials = new List<PartInfo> { null };
                    foreach (PartInfo r in mix.Tanks)
                        if (!r.NoseTop && CraftBuilder.DiameterOf(SizeOf(r)) <= 0.5 * D + 0.01 && HeightOf(r) <= coreH + engineH + 0.01)
                            radials.Add(r);
                    foreach (PartInfo r in radials)
                    {
                        if ((r == null) != alone) continue;
                        foreach (int n in r == null ? new[] { 0 } : new[] { 2, 3, 4, 6, 8 })
                        {
                            double dr = r != null ? CraftBuilder.DiameterOf(SizeOf(r)) : 0;
                            if (n * dr > 0.9 * Math.PI * (D + dr)) continue;   // side by side around the central tank
                            int legCount = LegCountFor(n);
                            s.Fuel = cores * Propellant(core) + (r != null ? n * Propellant(r) : 0);
                            s.TankDry = cores * core.DryMassT + (r != null ? n * r.DryMassT : 0);
                            s.LegCount = legCount;
                            if (LanderDeltaV(s, legs) < req.LanderDeltaV - 1) continue;
                            double m0 = req.PayloadMass + s.TotalMass - shield + legCount * legMass;
                            if (s.EngineCount * s.Engine.ThrustVacKN / (m0 * req.LanderGravity) < req.LanderTwr) break;
                            double cost = s.EngineCount * s.Engine.Cost + cores * core.Cost + (r != null ? n * r.Cost : 0);
                            double height = Math.Max(coreH, r != null ? HeightOf(r) : 0) + engineH + attitude;
                            double width = D + 2 * dr;
                            double score = (req.MinimizeCost ? cost : m0) * (1 + LanderAspectWeight * height / width)
                                * (1 + LanderPartWeight * n) * (core.Storage ? StorageBonus : 1);
                            if (score >= bestScore) continue;
                            bestScore = score;
                            bestCore = core; bestCores = cores; bestRadial = r; bestCount = n;
                            break;
                        }
                    }
                }
            }
            if (bestCore == null) { s.Fuel = oldFuel; s.TankDry = oldDry; s.LegCount = oldLegs; return false; }
            s.Size = SizeOf(bestCore);
            s.Tanks = new List<TankUse>();
            for (int i = 0; i < bestCores; i++) AddTank(s.Tanks, bestCore);
            s.RadialTank = bestRadial;
            s.RadialCount = bestCount;
            s.LegCount = LegCountFor(bestCount);
            s.Fuel = bestCores * Propellant(bestCore) + (bestRadial != null ? bestCount * Propellant(bestRadial) : 0);
            s.TankDry = bestCores * bestCore.DryMassT + (bestRadial != null ? bestCount * bestRadial.DryMassT : 0);
            s.HeightM = Math.Max(bestCores * HeightOf(bestCore), bestRadial != null ? HeightOf(bestRadial) : 0) + engineH + attitude;
            s.WidthM = CraftBuilder.DiameterOf(s.Size) + (bestRadial != null ? 2 * CraftBuilder.DiameterOf(SizeOf(bestRadial)) : 0);
            return true;
        }

        /// <summary>Legs: one per radial tank (on its outer side) for 3 or 4 of them, one every other tank for 6 or 8, else 4.</summary>
        static int LegCountFor(int radials)
        {
            return radials == 3 || radials == 4 ? radials : radials == 6 || radials == 8 ? radials / 2 : LanderLegs;
        }

        const double ShieldEntrySpeed = 1500;   // m/s: below that, an entry from low orbit barely heats up
        const double ChuteAreaPerDrag = 1.07;   // m² of drag area per unit of fullyDeployedDrag (one Mk16 lands a 1.2 t capsule at 6 m/s on Kerbin)
        const double TouchdownSpeed = 8;        // m/s under the main chutes

        /// <summary>
        /// Entry into an atmosphere: a heat shield under the lander when the entry is fast (wide enough to cover the radial
        /// tanks if one is), a ring of main chutes (one per leg position, the type that slows the lander the most for its
        /// mass) and drogues that open first. The engine finishes the landing when the air is too thin for the chutes.
        /// </summary>
        void PlanReentry(StagePlan s, PartInfo legs)
        {
            s.Chute = s.Drogue = s.HeatShield = null;
            s.Chutes = s.Drogues = 0;
            s.TouchdownSpeed = 0;
            if (req.LanderPressureAtm <= 0) return;

            if (req.LanderEntrySpeed >= ShieldEntrySpeed)
            {
                double core = CraftBuilder.DiameterOf(s.Size);
                foreach (PartInfo h in req.HeatShields)
                {
                    double w = Math.Max(h.DiameterM, CraftBuilder.DiameterOf(SizeOf(h)));
                    if (w < core - 0.01) continue;
                    double best = s.HeatShield == null ? 0 : Math.Max(s.HeatShield.DiameterM, CraftBuilder.DiameterOf(SizeOf(s.HeatShield)));
                    bool covers = w >= s.WidthM - 0.01, bestCovers = s.HeatShield != null && best >= s.WidthM - 0.01;
                    // The narrowest that covers the whole lander, otherwise the widest.
                    if (s.HeatShield == null || (covers && (!bestCovers || w < best)) || (!covers && !bestCovers && w > best)) s.HeatShield = h;
                }
            }

            int k = s.LegCount > 0 ? s.LegCount : LanderLegs;
            double mass = (req.PayloadMass + s.DryMass + s.Fuel + s.EngineFuel + s.LegCount * (legs != null ? legs.DryMassT : 0)) * 1000;   // kg
            double rho = 1.225 * req.LanderPressureAtm;
            double bestSpeed = double.MaxValue, bestMass = 0;
            foreach (PartInfo c in req.Parachutes)
            {
                if (c.Drogue) continue;
                double v = Math.Sqrt(2 * mass * req.LanderGravity / (rho * k * c.ChuteDrag * ChuteAreaPerDrag));
                // Slow enough: the lightest; otherwise the one that slows the most.
                bool ok = v <= TouchdownSpeed, bestOk = bestSpeed <= TouchdownSpeed;
                if (s.Chute == null || (ok && (!bestOk || c.DryMassT < bestMass)) || (!ok && !bestOk && v < bestSpeed))
                { s.Chute = c; bestSpeed = v; bestMass = c.DryMassT; }
            }
            if (s.Chute == null) return;
            s.Chutes = k;
            s.TouchdownSpeed = bestSpeed;
            foreach (PartInfo c in req.Parachutes)
                if (c.Drogue && (s.Drogue == null || c.DryMassT < s.Drogue.DryMassT)) s.Drogue = c;
            if (s.Drogue != null) s.Drogues = k;
        }

        /// <summary>TWR at the landing burn's ignition (heat shield gone).</summary>
        double LanderTwrNow(StagePlan s, PartInfo legs)
        {
            double m = req.PayloadMass + s.TotalMass - (s.HeatShield != null ? s.HeatShield.WetMassT : 0) + s.LegCount * (legs != null ? legs.DryMassT : 0);
            return s.EngineCount * s.Engine.ThrustVacKN / (m * req.LanderGravity);
        }

        double LanderTankCost(StagePlan s)
        {
            double cost = 0;
            foreach (TankUse t in s.Tanks) cost += t.Count * t.Tank.Cost;
            if (s.RadialTank != null) cost += s.RadialCount * s.RadialTank.Cost;
            return cost;
        }

        /// <summary>Smallest diameter where the propellant fits in at most two tanks: a squat lander, not a tower.</summary>
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
            int count = s.LegCount > 0 ? s.LegCount : LanderLegs;
            double dry = req.PayloadMass + s.DryMass + count * (legs != null ? legs.DryMassT : 0);
            return s.Engine.IspVac * G0 * Math.Log((dry + s.Fuel + s.EngineFuel) / dry);
        }

        List<LauncherDesign> RunLauncher()
        {
            List<LauncherDesign> result = new List<LauncherDesign>();
            if (req.TargetDeltaV <= 0) return result;
            List<Upper> front = UpperFront();
            Trace("front " + front.Count);

            pool.Clear(); rejections.Clear(); Ssto();
            result.Add(Finish("SSTO", "A single stage, no staging."));
            pool.Clear(); rejections.Clear(); TwoStages(front, null);
            result.Add(Finish("Two stages", "Two stacked stages, no boosters."));
            pool.Clear(); rejections.Clear(); TwoStages(front, "solid");
            result.Add(Finish("Solid boosters", "Two stages and solid motors dropped in flight."));
            pool.Clear(); rejections.Clear(); TwoStages(front, "liquid");
            result.Add(Finish("Liquid boosters", "Two stages and liquid boosters dropped together."));
            pool.Clear(); rejections.Clear(); TwoStages(front, "asparagus");
            result.Add(Finish("Asparagus",
                "Liquid boosters dropped 2 by 2; each pair feeds the rest of the launcher before being dropped."));
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
            public List<Upper> Uppers = new List<Upper>();   // the best upper stages for this core
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
        // Core alone at liftoff, carrying the whole stage's propellant: with boosters it carries much less.
        // Heavy payloads need low values here (the boosters do the lifting), below this the core could not keep up.
        const double MinCoreTwr = 0.25;

        // Best pairs (upper stage, first stage) ignoring liftoff TWR:
        // the boosters are precisely there to fix it.
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
                            // A cluster that cannot be mounted would only be rejected later, with every booster tried on it:
                            // it would take the place of a core that can be built.
                            if (n > 1 && !ClusterFits(engineMixture[e], e, n)) continue;
                            // A few upper stages per first-stage engine, to keep varied cores.
                            Core same = cores.Find(c => c.Engine == e && c.Count == n);
                            if (same == null) cores.Add(same = new Core { Engine = e, Count = n });
                            same.Add(u, Score(d));
                        }
                cores.Sort((a, b) => a.Score.CompareTo(b.Score));
                if (cores.Count > CoresForBoosters) cores.RemoveRange(CoresForBoosters, cores.Count - CoresForBoosters);
                return best;
            }

            Trace(boosterKind + ": " + cores.Count + " cores");
            foreach (Core core in cores)
            foreach (Upper u in core.Uppers)
            {
                PartInfo e = core.Engine;
                int n = core.Count;
                if (boosterKind == "solid")
                {
                    foreach (PartInfo srb in solidBoosters)
                        foreach (int c in BoosterCounts)
                            best = Better(best, Build(u, e, n, new BoosterPlan
                            {
                                Count = c, Solid = true, Part = srb, Decoupler = radial,
                                DecouplerMass = radial != null ? radial.DryMassT : 0.025,
                            }));
                    continue;
                }
                bool asparagus = boosterKind == "asparagus";
                // The boosters' engine need not be the core's: a more powerful one lifts more.
                // Asparagus boosters feed the core, so they burn the same propellants.
                foreach (PartInfo be in BoosterEngines())
                {
                    if (asparagus && engineMixture[be] != engineMixture[e]) continue;
                    // Booster count × engines each: once some total works, far bigger totals only add mass.
                    int firstValid = 1000;   // no valid total yet
                    for (int k = 1; k <= MaxBoosterEngines; k++)
                    {
                        if (k > 1 && !ClusterFits(engineMixture[be], be, k)) break;
                        if (k > 2 * firstValid) break;
                        foreach (int c in asparagus ? AsparagusCounts : BoosterCounts)
                        {
                            if (c * k > 2 * firstValid) break;
                            foreach (double share in BoosterShares)
                            {
                                LauncherDesign d = Build(u, e, n, new BoosterPlan
                                {
                                    Count = c, Solid = false, Crossfeed = asparagus, Part = be, Mixture = engineMixture[be], EnginesEach = k, Share = share,
                                    Decoupler = radial, DecouplerMass = radial != null ? radial.DryMassT : 0.025,
                                });
                                if (d.Stages.Count > 0 && ConstraintProblem(d) == null) firstValid = Math.Min(firstValid, c * k);
                                best = Better(best, d);
                            }
                        }
                    }
                }
            }
            return best;
        }

        List<PartInfo> boosterEngines;

        /// <summary>
        /// Engines worth trying on liquid boosters: those that no other engine with the same propellants beats
        /// at once on sea-level thrust, sea-level Isp, thrust-to-weight and diameter (smaller clusters better).
        /// </summary>
        List<PartInfo> BoosterEngines()
        {
            if (boosterEngines != null) return boosterEngines;
            boosterEngines = new List<PartInfo>();
            foreach (PartInfo a in lowerEngines)
            {
                bool dominated = false;
                if (Propellant(a) <= 0)
                    foreach (PartInfo b in lowerEngines)
                    {
                        if (b == a || engineMixture[b] != engineMixture[a] || Propellant(b) > 0) continue;
                        double ta = a.ThrustAslKN / a.DryMassT, tb = b.ThrustAslKN / b.DryMassT;
                        double da = CraftBuilder.DiameterOf(SizeOf(a)), db = CraftBuilder.DiameterOf(SizeOf(b));
                        if (b.ThrustAslKN >= a.ThrustAslKN && b.IspAsl >= a.IspAsl && tb >= ta && db <= da + 0.01 &&
                            (b.ThrustAslKN > a.ThrustAslKN || b.IspAsl > a.IspAsl || tb > ta || db < da - 0.01)) { dominated = true; break; }
                    }
                if (!dominated) boosterEngines.Add(a);
            }
            Trace("booster engines " + boosterEngines.Count + " / " + lowerEngines.Count);
            return boosterEngines;
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

        // ---------- Conversion to real parts ----------

        /// <summary>Replaces the continuous propellant quantities with real tanks, from top to bottom.</summary>
        LauncherDesign Finish(string variant, string description)
        {
            Trace(variant + ": " + pool.Count + " candidates");
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
                // Boosters standing above the core: give them less propellant (dropped earlier) until they fit.
                BoosterPlan cb = candidate.Stages[0].Boosters;
                for (int attempt = 0; attempt < 5 && !d.Valid && d.BoosterExcessM > 0.1 && cb != null && !cb.Solid; attempt++)
                {
                    LauncherDesign retry = Clone(candidate);
                    BoosterPlan rb = retry.Stages[0].Boosters;
                    double tall = d.HeightOfBoostersM > 0 ? d.HeightOfBoostersM : 1;
                    rb.Share = cb.Share * Math.Max(0.2, Math.Min(0.9, (tall - d.BoosterExcessM - 0.3) / tall));
                    cb = rb;
                    if (!SolveFirstStage(retry)) break;
                    d = Quantized(retry, variant, description);
                }
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
            // Too tall for the VAB: start over with wider, hence shorter, tanks.
            for (widen = 0; widen <= MaxWiden; widen++)
            {
                // Top to bottom: each stage is at least as wide as the one it carries.
                for (int i = d.Stages.Count - 1; i >= 1; i--)
                {
                    StagePlan u = d.Stages[i];
                    u.Fuel = fuels[i];
                    Quantize(d, i);
                    if (u.Attitude != null && u.Attitude.Mass > 0)
                    {
                        // The wheels and RCS weigh on the stage: more propellant keeps its delta-v (same mass ratio).
                        double end = req.PayloadMass + (d.Lander != null ? d.Lander.TotalMass : 0) + u.DryMass;
                        for (int j = i + 1; j < d.Stages.Count; j++) end += d.Stages[j].TotalMass;
                        u.Fuel = fuels[i] * (1 + u.Attitude.Mass / Math.Max(1, end - u.Attitude.Mass));
                        Quantize(d, i);
                    }
                }
                d.PayloadDecoupler = PayloadDecoupler(d.Stages[d.Stages.Count - 1].Size);
                SolveFirstStage(d);
                // The first stage's wheels and RCS depend on its tanks: place them once, then solve again with their mass.
                if (req.ReactionWheels.Count > 0 || req.RcsThrusters.Count > 0)
                {
                    Quantize(d, 0);
                    if (first.Attitude != null) SolveFirstStage(d);
                }
                double wanted = first.Fuel;
                first.Coupler = Coupler(first.Size, first.Engine, first.EngineCount);
                first.CouplerMass = first.Coupler != null ? first.Coupler.DryMassT : 0;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    SetFuel(first, wanted);
                    Quantize(d, 0);
                    first.Coupler = Coupler(first.Size, first.Engine, first.EngineCount);
                    first.CouplerMass = first.Coupler != null ? first.Coupler.DryMassT : 0;
                    double dv = Simulate(d);
                    if (dv >= req.TargetDeltaV) break;
                    wanted = Math.Max(wanted * 1.01, first.Fuel * (1 + (req.TargetDeltaV - dv) / req.TargetDeltaV));
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
            if (d.DeltaV < req.TargetDeltaV - 1) problem = string.Format("insufficient Δv once the tanks are rounded ({0:0} m/s)", d.DeltaV);
            if (problem == null) problem = SiteProblem(d);
            // Like real launchers, the boosters are shorter than the core (Sebastien's rule).
            if (problem == null && d.BoosterExcessM > 0.1)
                problem = string.Format("boosters taller than the core ({0} above it)", Metres(d.BoosterExcessM));
            d.Valid = problem == null;
            if (!d.Valid) d.Problem = problem;
            for (int i = 0; i < d.Stages.Count; i++)
            {
                StagePlan s = d.Stages[i];
                if (s.Decoupler == null && s != first)
                    d.Notes.Add("No unlocked decoupler of the right size.");
                if (s.EngineCount > 1 && s.Coupler == null && ClusterCapacity(CraftBuilder.DiameterOf(s.Size), CraftBuilder.DiameterOf(SizeOf(s.Engine))) < s.EngineCount)
                    d.Notes.Add(string.Format("Stage {0}: {1} engines overlap under a {2} tank (no wider tank unlocked).",
                        i + 1, s.EngineCount, Diameter(s.Size)));
                if (i + 1 < d.Stages.Count && s.Size != d.Stages[i + 1].Size &&
                    (s.Tanks.Count == 0 || StackSizes(s.Tanks[0].Tank) != 2))
                    d.Notes.Add(string.Format("No {0} → {1} adapter tank unlocked: add a structural adapter between stages {2} and {3}.",
                        Diameter(s.Size), Diameter(d.Stages[i + 1].Size), i + 1, i + 2));
                BoosterPlan b = s.Boosters;
                if (b != null && b.Count > 0 && b.NoseCone == null)
                {
                    PartInfo top = b.Solid ? b.Part : b.Tanks.Count > 0 ? b.Tanks[0].Tank : null;
                    if (top != null && !top.NoseTop)
                        d.Notes.Add(string.Format("No nose cone unlocked at Ø {0} for the boosters, nor an adapter toward a cone: their top stays flat.", Diameter(TopSize(top))));
                }
            }
            return d;
        }

        string Unavailable(string variant)
        {
            if (variant == "Solid boosters" && solidBoosters.Count == 0) return "No solid motor unlocked.";
            if (lowerEngines.Count == 0) return "No engine usable at liftoff is unlocked.";
            string main = null;
            int count = 0;
            foreach (KeyValuePair<string, int> r in rejections)
                if (r.Value > count) { count = r.Value; main = r.Key; }
            string text = "No combination of unlocked parts reaches the requested Δv" +
                (main != null ? "; most often: " + main + "." : " with a sufficient TWR.");
            // Without boosters, liftoff thrust is capped by the number of engines that fit under the widest tank.
            if (main == "liftoff TWR too low" && (variant == "SSTO" || variant == "Two stages"))
            {
                PartInfo engine;
                int engines;
                double thrust = MaxCoreThrust(out engine, out engines);
                if (engine != null)
                    text += string.Format(" Without boosters, liftoff thrust is capped at {0:0} kN ({1} × {2} under the widest unlocked tank, Ø {3}), " +
                        "i.e. at most {4:0} t at liftoff: boosters are needed, or unlock wider tanks.",
                        thrust, engines, engine.Title, Diameter(LargestSize(engineMixture[engine], SizeOf(engine))),
                        thrust / (req.HomeGravity * req.MinLiftoffTwr));
            }
            return text;
        }

        /// <summary>Highest sea-level thrust of a first stage without boosters: engine × the number that fits under the widest tank.</summary>
        double MaxCoreThrust(out PartInfo engine, out int count)
        {
            engine = null;
            count = 0;
            double best = 0;
            foreach (PartInfo e in lowerEngines)
                for (int n = MaxLowerEngines; n >= 1; n--)
                {
                    if (n > 1 && !ClusterFits(engineMixture[e], e, n)) continue;
                    double t = n * (req.HomeAtmosphere ? e.ThrustAslKN : e.ThrustVacKN);
                    if (t > best) { best = t; engine = e; count = n; }
                    break;
                }
            return best;
        }

        public static string Diameter(string size)
        {
            return CraftBuilder.DiameterOf(size).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " m";
        }

        /// <summary>How many engines of this size fit under a tank of this diameter (cluster, multi-coupler).</summary>
        static int ClusterCapacity(double tankDiameter, double engineDiameter)
        {
            if (tankDiameter < engineDiameter - 0.01) return 0;
            double k = tankDiameter / engineDiameter;
            return Math.Max(1, (int)Math.Floor(k * k + 1e-9));
        }

        /// <summary>
        /// Replaces the stage's propellant with real tanks. The diameter is the smallest that accommodates the engines,
        /// stays at least as wide as the stage above and fits in few tanks; if the stage above is narrower,
        /// an adapter tank caps the stage.
        /// </summary>
        void Quantize(LauncherDesign d, int index)
        {
            StagePlan s = d.Stages[index];
            string above = index + 1 < d.Stages.Count ? d.Stages[index + 1].Size : null;
            // The top stage is at least as wide as the lander it carries: no wide part on a thin stage.
            double minD = above != null ? CraftBuilder.DiameterOf(above) : topMinDiameter;
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
                // The decoupler under an upper stage takes its diameter.
                s.Decoupler = StackDecoupler(s.Size) ?? s.Decoupler;
                s.DecouplerMass = s.Decoupler != null ? s.Decoupler.DryMassT : 0.05;
            }
            s.Attitude = PlanAttitude(d, index);

            BoosterPlan b = s.Boosters;
            if (b != null && !b.Solid)
            {
                int k = b.EnginesEach;
                double engD = CraftBuilder.DiameterOf(SizeOf(b.Part));
                string bs = ChooseSize(b.Fuel, b.Mixture, b.Part, k, 0, CraftBuilder.DiameterOf(s.Size));
                // A booster taller than the core would stick out below: widen it until it fits.
                double coreHeight = TanksHeight(s.Tanks) + HeightOf(s.Engine) + (s.Attitude != null ? s.Attitude.StackHeight : 0);
                Mixture bm;
                if (heightsKnown && coreHeight > 0 && mixtures.TryGetValue(b.Mixture, out bm))
                    foreach (string size in bm.Sizes)
                    {
                        double bd = CraftBuilder.DiameterOf(size);
                        if (bd < CraftBuilder.DiameterOf(bs) - 0.01 || bd > CraftBuilder.DiameterOf(s.Size) + 0.01) continue;
                        if (ClusterCapacity(bd, engD) < k) continue;
                        double bf, btd;
                        List<TankUse> bt = Fill(b.Fuel, b.Mixture, size, out bf, out btd, true);
                        if (bt.Count > 0 && TanksHeight(bt) + HeightOf(b.Part) <= 0.95 * coreHeight) { bs = size; break; }
                    }
                // A booster gets a nose cone: if none is unlocked at its diameter, the nearest diameter
                // that has one and keeps the booster no taller than the core.
                if (req.NoseCones.Count > 0 && NoseChain(bs) == null && FuelNoseAdapter(b.Mixture, bs, b.Fuel) == null && mixtures.TryGetValue(b.Mixture, out bm))
                {
                    string coned = null;
                    double gap = double.MaxValue;
                    foreach (string size in bm.Sizes)
                    {
                        double bd = CraftBuilder.DiameterOf(size);
                        if (bd > CraftBuilder.DiameterOf(s.Size) + 0.01 || (NoseChain(size) == null && FuelNoseAdapter(b.Mixture, size, b.Fuel) == null)) continue;
                        if (ClusterCapacity(bd, engD) < k) continue;
                        double bf, btd;
                        List<TankUse> bt = Fill(b.Fuel, b.Mixture, size, out bf, out btd, true);
                        if (bt.Count == 0 || TankParts(bt) > MaxTankParts) continue;
                        if (heightsKnown && coreHeight > 0 && TanksHeight(bt) + HeightOf(b.Part) > 0.95 * coreHeight) continue;
                        double g = Math.Abs(bd - CraftBuilder.DiameterOf(bs));
                        if (g < gap) { gap = g; coned = size; }
                    }
                    if (coned != null) bs = coned;
                }
                // No cone at this size: an adapter tank on top narrows the booster and carries propellant.
                PartInfo noseTank = FuelNoseAdapter(b.Mixture, bs, b.Fuel);
                if (noseTank != null)
                {
                    double nf, ntd;
                    List<TankUse> rest = Fill(b.Fuel - Propellant(noseTank), b.Mixture, bs, out nf, out ntd, true);
                    b.Tanks = new List<TankUse> { new TankUse { Tank = noseTank, Count = 1 } };
                    b.Tanks.AddRange(rest);
                    b.Fuel = nf + Propellant(noseTank);
                    b.TankDry = ntd + noseTank.DryMassT;
                }
                else b.Tanks = Fill(b.Fuel, b.Mixture, bs, out b.Fuel, out b.TankDry, true);
                b.Coupler = Coupler(bs, b.Part, k);
                b.CouplerMass = b.Coupler != null ? b.Coupler.DryMassT : 0;
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
            // Nothing fits (no tank wide enough): the widest available.
            if (sizes.Count == 0) return mix.Sizes[mix.Sizes.Count - 1];
            // Among the diameters that fit in few tanks, the one that adds the least useless mass
            // (dry mass, excess propellant once rounded to real tanks, and a little per part).
            int chosen = -1, fewest = int.MaxValue, fewestAt = 0;
            double best = double.MaxValue;
            for (int i = 0; i < sizes.Count; i++)
            {
                double got, dry;
                int parts = TankParts(Fill(fuel, mixture, sizes[i], out got, out dry));
                if (parts < fewest) { fewest = parts; fewestAt = i; }
                if (parts > MaxTankParts) continue;
                // A stage wider than necessary weighs little, but a rocket look wants stages that narrow toward the top.
                double score = FillScore(fuel, got, dry, parts) + 0.03 * fuel * i;
                if (score < best - 1e-9) { best = score; chosen = i; }
            }
            if (chosen < 0) chosen = fewestAt;
            return sizes[Math.Min(chosen + widen, sizes.Count - 1)];
        }

        // ---------- Size (VAB and launch pad) ----------

        const int MaxWiden = 3;
        int widen;   // diameter steps added when the launcher is too tall

        // ---------- Attitude control ----------

        // Wheels: turn the stage and everything above it at 0.002 rad/s² (90° in about a minute), engines off.
        const double WheelAccel = 0.002;
        // RCS: about 0.015 m/s² of translation, and 3 m/s of monopropellant for the stack.
        const double RcsAccel = 0.015;
        const double RcsDeltaV = 3;
        const int MaxWheels = 3;
        const double CoastFromShare = 0.7;   // of the ascent delta-v: past this, the stage coasts or circularizes

        static bool MonoOnly(PartInfo p)
        {
            if (p.Kind != PartKind.Tank || p.Resources.Count == 0 || p.WetMassT <= p.DryMassT) return false;
            foreach (ResourceAmount r in p.Resources) if (r.Name != "MonoPropellant") return false;
            return true;
        }

        /// <summary>
        /// Reaction wheels and RCS for stage `index`, sized to what it must turn: itself full and all that sits above it
        /// (rod of the same mass and length). The wheels go between the two tanks closest to the stage's center of mass.
        /// </summary>
        AttitudePlan PlanAttitude(LauncherDesign d, int index)
        {
            StagePlan s = d.Stages[index];
            List<PartInfo> stack = new List<PartInfo>();
            foreach (TankUse t in s.Tanks) for (int n = 0; n < t.Count; n++) stack.Add(t.Tank);
            if (stack.Count == 0 || (req.ReactionWheels.Count == 0 && req.RcsThrusters.Count == 0)) return null;
            // A stage that burns out low in the atmosphere is steered by its gimbals and never coasts: nothing to add.
            if (req.HomeAtmosphere && s.StartDeltaV + s.DeltaV < CoastFromShare * req.AscentDeltaV) return null;

            // Mass and length of what the stage carries once the stages below are gone.
            double mass = req.PayloadMass + (d.PayloadDecoupler != null ? d.PayloadDecoupler.DryMassT : 0) + (d.Lander != null ? d.Lander.TotalMass : 0);
            double length = req.PayloadHeight + HeightOf(d.PayloadDecoupler);
            for (int i = index; i < d.Stages.Count; i++)
            {
                StagePlan u = d.Stages[i];
                mass += u.EngineCount * u.Engine.WetMassT + u.TankDry + u.Fuel + u.DecouplerMass + u.CouplerMass;
                length += TanksHeight(u.Tanks) + HeightOf(u.Engine) + (i > 0 ? HeightOf(u.Decoupler) : 0);
                if (i > index && u.Attitude != null) { mass += u.Attitude.Mass; length += u.Attitude.StackHeight; }
            }
            return PlanAttitude(s, stack, mass, length);
        }

        /// <summary>
        /// The lander steers itself and the payload during the descent and the trip back: RCS between its radial tanks,
        /// wheels above its central tank when the payload lacks some.
        /// </summary>
        AttitudePlan PlanLanderAttitude(StagePlan s, PartInfo legs)
        {
            List<PartInfo> stack = new List<PartInfo>();
            foreach (TankUse t in s.Tanks) for (int n = 0; n < t.Count; n++) stack.Add(t.Tank);
            if (stack.Count == 0 || (req.ReactionWheels.Count == 0 && req.RcsThrusters.Count == 0)) return null;
            double mass = req.PayloadMass + s.TotalMass - (s.Attitude != null ? s.Attitude.Mass : 0) + s.LegCount * (legs != null ? legs.DryMassT : 0);
            double length = req.PayloadHeight + s.HeightM - (s.Attitude != null ? s.Attitude.StackHeight : 0);
            return PlanAttitude(s, stack, mass, length);
        }

        AttitudePlan PlanAttitude(StagePlan s, List<PartInfo> stack, double mass, double length)
        {
            if (length <= 0) length = 10;
            double inertia = mass * length * length / 12;   // t·m²
            // The payload's own wheels help: the stage only adds what is missing.
            AttitudePlan a = new AttitudePlan { NeededTorqueKNm = Math.Max(0, inertia * WheelAccel - req.PayloadTorqueKNm) };

            // Center of mass of the stage itself (full), measured downward from the top of its first tank.
            double y = 0, moment = 0, total = 0;
            List<double> joints = new List<double>();
            foreach (PartInfo t in stack)
            {
                joints.Add(y);
                double h = HeightOf(t);
                moment += t.WetMassT * (y + h / 2);
                total += t.WetMassT;
                y += h;
            }
            double engines = s.EngineCount * s.Engine.WetMassT;
            moment += engines * (y + HeightOf(s.Engine) / 2);
            total += engines;
            double com = total > 0 ? moment / total : y / 2;

            // Wheels: at each joint (above a tank), the lightest set that gives the torque, or the strongest set.
            double bestDistance = double.MaxValue;
            for (int j = 0; j < stack.Count && a.NeededTorqueKNm > 0; j++)
            {
                string size = TopSize(stack[j]);
                PartInfo wheel = null;
                int count = 0;
                foreach (PartInfo w in req.ReactionWheels)
                {
                    if (w.TorqueKNm <= 0 || !HasSize(w, size)) continue;
                    int n = Math.Min(MaxWheels, Math.Max(1, (int)Math.Ceiling(a.NeededTorqueKNm / w.TorqueKNm)));
                    bool enough = n * w.TorqueKNm >= a.NeededTorqueKNm;
                    bool bestEnough = wheel != null && count * wheel.TorqueKNm >= a.NeededTorqueKNm;
                    if (wheel == null || (enough && !bestEnough) || (enough && bestEnough && n * w.DryMassT < count * wheel.DryMassT)
                        || (!enough && !bestEnough && n * w.TorqueKNm > count * wheel.TorqueKNm))
                    { wheel = w; count = n; }
                }
                if (wheel == null) continue;
                double distance = Math.Abs(joints[j] - com);
                if (distance < bestDistance) { bestDistance = distance; a.Wheel = wheel; a.Wheels = count; a.Joint = j; }
            }
            if (a.Wheel != null) a.TorqueKNm = a.Wheels * a.Wheel.TorqueKNm;
            else
            {
                // No wheel at the stage's size: the monopropellant tank still goes near the center of mass.
                for (int j = 0; j < stack.Count; j++)
                    if (Math.Abs(joints[j] - com) < bestDistance) { bestDistance = Math.Abs(joints[j] - com); a.Joint = j; }
            }

            // RCS: the gentlest thruster that still gives the acceleration with 4 nozzles pushing, or the strongest one.
            double needed = mass * RcsAccel / 4;   // kN per nozzle
            // A payload whose own RCS already pushes the whole stack twice as hard (about a quarter of its nozzles push
            // in any one direction) needs none on the stage.
            bool payloadRcs = req.PayloadRcsKN / 4 >= 2 * mass * RcsAccel;
            foreach (PartInfo r in payloadRcs ? new List<PartInfo>() : req.RcsThrusters)
            {
                if (r.RcsThrustKN <= 0) continue;
                bool enough = r.RcsThrustKN >= needed, bestEnough = a.Rcs != null && a.Rcs.RcsThrustKN >= needed;
                if (a.Rcs == null || (enough && (!bestEnough || r.RcsThrustKN < a.Rcs.RcsThrustKN || (r.RcsThrustKN == a.Rcs.RcsThrustKN && r.DryMassT < a.Rcs.DryMassT)))
                    || (!enough && !bestEnough && r.RcsThrustKN > a.Rcs.RcsThrustKN))
                    a.Rcs = r;
            }
            if (a.Rcs != null)
            {
                // Two rings (top and bottom of the tanks) when one is not strong enough and the stage is long enough;
                // a single ring goes at the bottom, the farthest from the stack's center of mass (the payload is above).
                a.Rings = y > 2 && a.Rcs.RcsThrustKN < 2 * needed ? 2 : 1;
                a.RcsPerRing = 4;
                a.RingAngle = 45;
                BoosterPlan b = s.Boosters;
                if (b != null && b.Count > 0)
                {
                    // Between the boosters: 4 when their count allows it (2, 4, 8), otherwise 3 (3, 6).
                    a.RcsPerRing = b.Count == 2 || b.Count % 4 == 0 ? 4 : 3;
                    a.RingAngle = b.Count == 2 ? 45 : 180.0 / b.Count;
                }
                else if (s.RadialCount > 0)
                {
                    // Lander: between its radial tanks.
                    a.RcsPerRing = s.RadialCount % 3 == 0 ? 3 : 4;
                    a.RingAngle = s.RadialCount == 2 ? 45 : 180.0 / s.RadialCount;
                }

                // Monopropellant: stacked with the wheels, or radial tanks in symmetry. The lightest choice, a missing
                // tonne of monopropellant counting as three tonnes too many (a big tank is not worth a small need).
                double isp = a.Rcs.RcsIsp > 0 ? a.Rcs.RcsIsp : 240;
                double mono = mass * RcsDeltaV / (isp * G0);
                string jointSize = TopSize(stack[a.Joint]);
                double bestScore = double.MaxValue;
                foreach (PartInfo p in req.Parts)
                {
                    if (!p.Unlocked || !MonoOnly(p)) continue;
                    bool radial = (p.Sizes ?? "").Trim() == "srf";
                    if (!radial && !HasSize(p, jointSize)) continue;
                    foreach (int n in radial ? new[] { 2, a.RcsPerRing } : new[] { 1 })
                    {
                        double carried = n * Propellant(p);
                        double score = n * p.WetMassT + 3 * Math.Max(0, mono - carried);
                        if (score < bestScore) { bestScore = score; a.MonoTank = p; a.MonoTanks = n; a.MonoRadial = radial; a.MonoT = carried; }
                    }
                }
            }
            a.StackHeight = (a.Wheel != null ? a.Wheels * HeightOf(a.Wheel) : 0) + (a.MonoTank != null && !a.MonoRadial ? HeightOf(a.MonoTank) : 0);
            return a.Wheel == null && a.Rcs == null ? null : a;
        }

        /// <summary>Stack height of a part; estimated from its mass when the game does not provide it.</summary>
        static double HeightOf(PartInfo p)
        {
            if (p == null) return 0;
            if (p.HeightM > 0) return p.HeightM;
            double dia = CraftBuilder.DiameterOf(SizeOf(p));
            if (p.Kind == PartKind.Decoupler) return 0.4;
            if (p.Kind == PartKind.Engine) return 1.2 * dia;
            double density = p.Kind == PartKind.Booster ? 1.6 : 1.0;   // t/m³: order of magnitude of stock parts
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

        /// <summary>Height, width and part count of the assembled launcher, payload included.</summary>
        void Measure(LauncherDesign d)
        {
            AssignBoosterParts(d);
            double height = req.PayloadHeight + HeightOf(d.PayloadDecoupler);
            double width = req.PayloadWidth;
            int parts = req.PayloadParts + (d.PayloadDecoupler != null ? 1 : 0);
            double boosterTop = 0;
            for (int i = 0; i < d.Stages.Count; i++)
            {
                StagePlan s = d.Stages[i];
                double stage = TanksHeight(s.Tanks) + HeightOf(s.Engine) + HeightOf(s.Coupler);
                if (i > 0 && s.Decoupler != null) stage += HeightOf(s.Decoupler);
                if (s.Attitude != null) stage += s.Attitude.StackHeight;
                height += stage;
                parts += TankParts(s.Tanks) + s.EngineCount + (i > 0 && s.Decoupler != null ? 1 : 0) + (s.Coupler != null ? 1 : 0)
                    + (s.Attitude != null ? s.Attitude.Parts : 0);
                double dia = CraftBuilder.DiameterOf(s.Size);
                width = Math.Max(width, dia);
                BoosterPlan b = s.Boosters;
                if (b != null && b.Count > 0)
                {
                    double bh = (b.Solid ? HeightOf(b.Part) : TanksHeight(b.Tanks) + HeightOf(b.Part) + HeightOf(b.Coupler)) + HeightOf(b.NoseCone);
                    foreach (PartInfo a in b.NoseAdapters) bh += HeightOf(a);
                    string bs = b.Solid || b.Tanks.Count == 0 ? SizeOf(b.Part) : SizeOf(b.Tanks[0].Tank);
                    boosterTop = Math.Max(boosterTop, bh);   // the boosters are aligned with the bottom of the first stage
                    if (i == 0) { d.BoosterExcessM = Math.Max(0, bh - stage); d.HeightOfBoostersM = bh; }
                    width = Math.Max(width, dia + 2 * CraftBuilder.DiameterOf(bs) + 0.2);
                    parts += b.Count * ((b.Solid ? 0 : TankParts(b.Tanks) + b.EnginesEach - 1 + (b.Coupler != null ? 1 : 0)) + 1 + (b.Decoupler != null ? 1 : 0) + b.SeparatorsEach + (b.Crossfeed ? 1 : 0) + (b.NoseCone != null ? 1 : 0) + b.NoseAdapters.Count);
                }
            }
            d.HeightM = Math.Max(height, boosterTop);
            d.WidthM = width;
            d.PartCount = parts;
        }

        static string Metres(double m)
        {
            return m.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " m";
        }

        /// <summary>What prevents building or launching this launcher with the current buildings, or null.</summary>
        string SiteProblem(LauncherDesign d)
        {
            Measure(d);
            if (req.MaxHeight > 0 && d.HeightM > req.MaxHeight)
                return string.Format("too tall: {0} against {1} allowed (upgrade the launch pad or lighten the payload)", Metres(d.HeightM), Metres(req.MaxHeight));
            if (req.MaxWidth > 0 && d.WidthM > req.MaxWidth)
                return string.Format("too wide: {0} against {1} allowed", Metres(d.WidthM), Metres(req.MaxWidth));
            if (req.MaxParts > 0 && d.PartCount > req.MaxParts)
                return string.Format("too many parts: {0} against {1} allowed by the VAB", d.PartCount, req.MaxParts);
            if (req.MaxMass > 0 && d.TotalMass > req.MaxMass)
                return string.Format("too heavy for the launch pad: {0:0.#} t against {1:0.#} t allowed", d.TotalMass, req.MaxMass);
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

        /// <summary>Chooses tanks for at least `fuel` tonnes of propellant: the biggest first, then the smallest that suffices.</summary>
        /// <summary>"Useless" mass of a fill: dry mass, excess or missing propellant, and a little per part.</summary>
        static double FillScore(double fuel, double got, double dry, int parts)
        {
            double extra = got - fuel;
            return dry + (extra >= 0 ? 0.5 * extra : 3 * -extra) + 0.02 * fuel * parts;
        }

        /// <param name="uniform">A single tank model (boosters): no tank from another family for the remainder.</param>
        List<TankUse> Fill(double fuel, string mixture, string size, out double actualFuel, out double dry, bool uniform = false)
        {
            List<TankUse> used = new List<TankUse>();
            actualFuel = 0;
            dry = 0;
            double bestRatio = BestRatio(TanksFor(mixture, size));
            List<PartInfo> tanks = TanksFor(mixture, size).FindAll(t => t.DryMassT / Propellant(t) <= bestRatio * TankRatioSlack);
            if (tanks.Count == 0 || fuel <= 0) return used;
            // At equal capacity, the tank that is lightest when empty.
            tanks.Sort((a, b) =>
            {
                int c = Propellant(b).CompareTo(Propellant(a));
                return c != 0 ? c : (a.DryMassT / Propellant(a)).CompareTo(b.DryMassT / Propellant(b));
            });

            // A repeated main tank, plus if needed a single tank for the remainder:
            // few parts, a regular stack, as little excess propellant as possible.
            double bestScore = double.MaxValue;
            PartInfo bestMain = null, bestRest = null;
            int bestCount = 0;
            foreach (PartInfo main in tanks)
            {
                // A nose tank only goes on top of a booster, alone: never under another part.
                if (main.NoseTop && !uniform) continue;
                double p = Propellant(main);
                int whole = (int)Math.Floor(fuel / p + 1e-9);
                if (whole > 200) continue;
                for (int n = Math.Max(whole, 1); n <= (main.NoseTop ? 1 : whole + 1); n++)
                {
                    double rest = fuel - n * p;
                    PartInfo cover = null;
                    if (rest > 0.02 * fuel)
                    {
                        if (uniform) continue;
                        // The smallest tank that covers the remainder.
                        foreach (PartInfo t in tanks) if (!t.NoseTop && Propellant(t) >= rest - 1e-9) cover = t;
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
