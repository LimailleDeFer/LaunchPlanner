using System;
using System.Collections.Generic;

namespace LaunchPlanner
{
    /// <summary>What the delta-v computation needs to know about a celestial body (independent of KSP, hence testable).</summary>
    public class BodyInfo
    {
        public string Name;            // displayed name (localized)
        public string Key;             // internal name (bodyName), stable regardless of language
        public double Mu;              // gravitational parameter (m³/s²)
        public double Radius;          // m
        public bool HasAtmosphere;
        public double AtmosphereDepth; // m
        public double PressureAtm;     // sea-level pressure, in atmospheres
        public double RotationPeriod;  // s
        public bool HasSurface = true;
        public bool IsStar;
        public double Sma;             // semi-major axis of the orbit around the parent (m)
        public BodyInfo Parent;

        public double LowOrbitRadius
        {
            get
            {
                double altitude = HasAtmosphere ? AtmosphereDepth + 10000 : Math.Max(10000, Radius * 0.05);
                return Radius + altitude;
            }
        }

        public double LowOrbitSpeed { get { return Math.Sqrt(Mu / LowOrbitRadius); } }
        public double SurfaceGravity { get { return Mu / (Radius * Radius); } }

        public int Depth
        {
            get { int d = 0; for (BodyInfo b = Parent; b != null; b = b.Parent) d++; return d; }
        }
    }

    public enum MissionType { Orbit, Landing, LandingReturn, OrbitReturn }

    public class Phase
    {
        public string Name;
        public double DeltaV;
        public string Note = "";
        public bool CoveredByPayload;
        public double CoveredDeltaV;
        public double LanderDeltaV;     // share provided by the lander stage
        public bool Landing;            // landing on the destination
    }

    public class Budget
    {
        public List<Phase> Phases = new List<Phase>();
        public List<string> Warnings = new List<string>();
        public double Total;
        public double TotalWithMargin;
        public double PayloadDeltaV;
        public double PayloadUsedDeltaV;   // share of the payload delta-v counted in the mission
        public double LauncherDeltaV;

        // Lander stage added by the launcher when the payload cannot land by itself
        public double LanderDeltaV;
        public double LanderGravity;
        public string LanderBody = "";
        public double LanderPressureAtm;   // 0: no atmosphere
        public double LanderEntrySpeed;
    }

    /// <summary>
    /// Delta-v budget of a mission using Hohmann transfers between circular, coplanar orbits
    /// (patched conics). It is the computed equivalent of a delta-v map: the margin covers
    /// plane changes, corrections and piloting.
    /// </summary>
    public static class MissionBudget
    {
        // Surface-to-low-orbit delta-v from the community map, for unscaled stock bodies.
        static readonly Dictionary<string, double[]> StockAscent = new Dictionary<string, double[]>
        {
            // name: { radius (m), delta-v (m/s) }
            { "Kerbin", new double[] { 600000, 3400 } },
            { "Mun", new double[] { 200000, 580 } },
            { "Minmus", new double[] { 60000, 180 } },
            { "Moho", new double[] { 250000, 870 } },
            { "Eve", new double[] { 700000, 8000 } },
            { "Gilly", new double[] { 13000, 30 } },
            { "Duna", new double[] { 320000, 1450 } },
            { "Ike", new double[] { 130000, 390 } },
            { "Dres", new double[] { 138000, 430 } },
            { "Laythe", new double[] { 500000, 2900 } },
            { "Vall", new double[] { 300000, 860 } },
            { "Tylo", new double[] { 600000, 2270 } },
            { "Bop", new double[] { 65000, 220 } },
            { "Pol", new double[] { 44000, 130 } },
            { "Eeloo", new double[] { 210000, 620 } },
        };

        /// <summary>Delta-v to take off and reach low orbit.</summary>
        public static double Ascent(BodyInfo b)
        {
            double[] stock;
            if (StockAscent.TryGetValue(b.Key ?? b.Name, out stock) && Math.Abs(stock[0] - b.Radius) < 1) return stock[1];

            double v = b.LowOrbitSpeed;
            if (!b.HasAtmosphere) return v * 1.05;
            double rotation = b.RotationPeriod > 0 ? 2 * Math.PI * b.Radius / b.RotationPeriod : 0;
            // Gravity and drag losses, calibrated on Kerbin (about 3,400 m/s).
            double gravityLoss = 0.3 * v;
            double dragLoss = 600 * Math.Pow(b.PressureAtm, 0.75);
            return v - rotation + gravityLoss + dragLoss;
        }

        /// <summary>Delta-v to land from low orbit. With an atmosphere, aerobraking and parachutes are assumed.</summary>
        public static double Landing(BodyInfo b, out string note)
        {
            note = "";
            if (!b.HasAtmosphere) return Ascent(b);
            note = "aerobraking and parachutes";
            return b.PressureAtm > 0.5 ? 0 : 0.15 * b.LowOrbitSpeed;
        }

        /// <summary>Delta-v between a low orbit and a hyperbolic excess speed vInf (ejection or capture).</summary>
        public static double Hyperbolic(BodyInfo b, double vInf)
        {
            double r = b.LowOrbitRadius;
            return Math.Sqrt(vInf * vInf + 2 * b.Mu / r) - Math.Sqrt(b.Mu / r);
        }

        /// <summary>Excess speeds at departure and arrival of a Hohmann transfer between two circular orbits around mu.</summary>
        public static void Hohmann(double mu, double r1, double r2, out double vDepart, out double vArrive)
        {
            double a = (r1 + r2) / 2;
            vDepart = Math.Abs(Math.Sqrt(mu * (2 / r1 - 1 / a)) - Math.Sqrt(mu / r1));
            vArrive = Math.Abs(Math.Sqrt(mu / r2) - Math.Sqrt(mu * (2 / r2 - 1 / a)));
        }

        /// <param name="payloadPhases">Phases the player assigns to the payload (one flag per phase),
        /// or null for the automatic split.</param>
        public static Budget Compute(BodyInfo home, BodyInfo dest, MissionType type, double margin, double payloadDeltaV,
            bool[] payloadPhases = null)
        {
            Budget budget = new Budget();
            bool landing = type == MissionType.Landing || type == MissionType.LandingReturn;
            bool back = type == MissionType.LandingReturn || type == MissionType.OrbitReturn;

            Add(budget, "Liftoff and low orbit of " + home.Name, Ascent(home), "");
            int arrival = 0;   // last phase of the trip to the destination's orbit

            if (dest != home)
            {
                double outbound, capture, inbound, returnCapture;
                string capNote;
                if (!Transfer(home, dest, out outbound, out capture, out inbound, out returnCapture, out capNote))
                {
                    budget.Warnings.Add("Trip between " + home.Name + " and " + dest.Name + " is not supported: only the liftoff is counted.");
                }
                else
                {
                    Add(budget, "Transfer injection to " + dest.Name, outbound, "");
                    Add(budget, "Orbit insertion at " + dest.Name, capture, capNote);
                    arrival = budget.Phases.Count - 1;
                    if (landing) AddLanding(budget, dest);
                    if (landing && back) Add(budget, "Liftoff from " + dest.Name, Ascent(dest), "");
                    if (back)
                    {
                        Add(budget, "Return to " + home.Name, inbound, "");
                        if (home.HasAtmosphere) Add(budget, "Re-entry at " + home.Name, 0, "aerobraking and parachutes");
                        else
                        {
                            Add(budget, "Orbit insertion at " + home.Name, returnCapture, "");
                            AddLanding(budget, home);
                        }
                    }
                }
            }
            else if (landing)
            {
                AddLanding(budget, home);
            }

            if (landing && !dest.HasSurface) budget.Warnings.Add(dest.Name + " has no solid surface: cannot land there.");

            foreach (Phase p in budget.Phases) budget.Total += p.DeltaV;
            budget.TotalWithMargin = budget.Total * (1 + margin);
            budget.PayloadDeltaV = Math.Max(0, payloadDeltaV);

            if (payloadPhases != null && payloadPhases.Length == budget.Phases.Count)
            {
                // Player's choice, phase by phase. The liftoff always stays with the launcher.
                double launcher = 0, payloadNeed = 0;
                for (int i = 0; i < budget.Phases.Count; i++)
                {
                    Phase p = budget.Phases[i];
                    double need = p.DeltaV * (1 + margin);
                    if (i == 0 || !payloadPhases[i]) launcher += need;
                    else
                    {
                        p.CoveredDeltaV = need;
                        p.CoveredByPayload = true;
                        payloadNeed += need;
                    }
                }
                budget.LauncherDeltaV = launcher;
                budget.PayloadUsedDeltaV = payloadNeed;
                if (payloadNeed > 0.5 && payloadDeltaV < payloadNeed - 0.5)
                    budget.Warnings.Add(string.Format("The payload only has {0:0} m/s for the {1:0} m/s of the phases you assign to it.",
                        payloadDeltaV, payloadNeed));
            }
            else
            {
                // The launcher brings the payload to the destination's orbit; the payload's delta-v is used for what it
                // does on site (landing, ascent, return), working back from the end, never for the trip.
                double remaining = budget.PayloadDeltaV;
                for (int i = budget.Phases.Count - 1; i > arrival && remaining > 0; i--)
                {
                    Phase p = budget.Phases[i];
                    double need = p.DeltaV * (1 + margin);
                    p.CoveredDeltaV = Math.Min(need, remaining);
                    p.CoveredByPayload = p.CoveredDeltaV >= need - 0.5;
                    remaining -= p.CoveredDeltaV;
                }
                budget.LauncherDeltaV = Math.Max(0, budget.TotalWithMargin - (budget.PayloadDeltaV - remaining));
                budget.PayloadUsedDeltaV = budget.PayloadDeltaV - remaining;
            }

            // What the payload does not cover from the landing onward goes to a lander stage:
            // a launcher's upper stage has neither legs nor the thrust needed to land.
            int landingPhase = budget.Phases.FindIndex(p => p.Landing);
            if (landing && landingPhase >= 1 && dest.HasSurface)
            {
                double lander = 0;
                for (int i = landingPhase; i < budget.Phases.Count; i++)
                {
                    Phase p = budget.Phases[i];
                    double missing = p.DeltaV * (1 + margin) - p.CoveredDeltaV;
                    if (missing > 0.5) { p.LanderDeltaV = missing; lander += missing; }
                }
                if (lander >= 30)
                {
                    budget.LanderDeltaV = lander;
                    budget.LanderGravity = dest.SurfaceGravity;
                    budget.LanderBody = dest.Name;
                    budget.LanderPressureAtm = dest.HasAtmosphere ? dest.PressureAtm : 0;
                    budget.LanderEntrySpeed = dest.LowOrbitSpeed;
                    budget.LauncherDeltaV = Math.Max(0, budget.LauncherDeltaV - lander);
                }
                else foreach (Phase p in budget.Phases) p.LanderDeltaV = 0;
            }
            return budget;
        }

        static void AddLanding(Budget budget, BodyInfo b)
        {
            string note;
            double dv = Landing(b, out note);
            Add(budget, "Landing at " + b.Name, dv, note);
            budget.Phases[budget.Phases.Count - 1].Landing = true;
        }

        static void Add(Budget budget, string name, double dv, string note)
        {
            budget.Phases.Add(new Phase { Name = name, DeltaV = dv, Note = note });
        }

        /// <summary>
        /// Computes the outbound (ejection, capture) and return (ejection, capture) delta-v between the low orbit
        /// of the departure body and that of the destination. Supported cases: moon of the departure body,
        /// sibling body (same parent), moon of a sibling body, and the reverse (departure from a moon to its parent).
        /// </summary>
        public static bool Transfer(BodyInfo home, BodyInfo dest, out double outbound, out double capture,
            out double inbound, out double returnCapture, out string captureNote)
        {
            outbound = capture = inbound = returnCapture = 0;
            captureNote = "";
            double vDep, vArr;

            if (dest.Parent == home)
            {
                // Moon of the departure body: transfer from low orbit to the moon's orbit.
                Hohmann(home.Mu, home.LowOrbitRadius, dest.Sma, out vDep, out vArr);
                outbound = vDep;
                capture = Hyperbolic(dest, vArr);
                inbound = capture;
                returnCapture = vDep;
                return true;
            }

            if (home.Parent != null && dest == home.Parent)
            {
                // From a moon to its parent.
                Hohmann(dest.Mu, home.Sma, dest.LowOrbitRadius, out vDep, out vArr);
                outbound = Hyperbolic(home, vDep);
                capture = vArr;
                inbound = vArr;
                returnCapture = outbound;
                return true;
            }

            if (home.Parent != null && dest.Parent == home.Parent)
            {
                // Sibling bodies: Hohmann transfer around the common parent.
                Hohmann(home.Parent.Mu, home.Sma, dest.Sma, out vDep, out vArr);
                outbound = Hyperbolic(home, vDep);
                capture = Hyperbolic(dest, vArr);
                inbound = capture;
                returnCapture = outbound;
                return true;
            }

            if (home.Parent != null && dest.Parent != null && dest.Parent.Parent == home.Parent)
            {
                // Moon of another planet: transfer to the planet, braking down to the moon's orbit,
                // then capture around the moon. No gravity assist, so conservative.
                BodyInfo planet = dest.Parent;
                Hohmann(home.Parent.Mu, home.Sma, planet.Sma, out vDep, out vArr);
                outbound = Hyperbolic(home, vDep);
                double r = dest.Sma;
                double planetBrake = Math.Sqrt(vArr * vArr + 2 * planet.Mu / r) - Math.Sqrt(planet.Mu / r);
                double moonCapture = Math.Sqrt(2 * dest.Mu / dest.LowOrbitRadius) - dest.LowOrbitSpeed;
                capture = planetBrake + moonCapture;
                if (planet.HasAtmosphere) captureNote = "aerocapture possible at " + planet.Name;
                inbound = capture;
                returnCapture = outbound;
                return true;
            }

            return false;
        }
    }
}
