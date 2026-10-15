using System;
using System.Collections.Generic;

namespace LaunchPlanner
{
    /// <summary>Ce que le calcul du Δv a besoin de savoir d'un corps céleste (indépendant de KSP, donc testable).</summary>
    public class BodyInfo
    {
        public string Name;            // nom affiché (localisé)
        public string Key;             // nom interne (bodyName), stable quelle que soit la langue
        public double Mu;              // paramètre gravitationnel (m³/s²)
        public double Radius;          // m
        public bool HasAtmosphere;
        public double AtmosphereDepth; // m
        public double PressureAtm;     // pression au niveau de la mer, en atmosphères
        public double RotationPeriod;  // s
        public bool HasSurface = true;
        public bool IsStar;
        public double Sma;             // demi-grand axe de l'orbite autour du parent (m)
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
        public double LanderDeltaV;     // part assurée par l'étage atterrisseur
        public bool Landing;            // atterrissage sur la destination
    }

    public class Budget
    {
        public List<Phase> Phases = new List<Phase>();
        public List<string> Warnings = new List<string>();
        public double Total;
        public double TotalWithMargin;
        public double PayloadDeltaV;
        public double PayloadUsedDeltaV;   // part du Δv de la charge utile comptée dans la mission
        public double LauncherDeltaV;

        // Étage atterrisseur ajouté par le lanceur quand la charge utile ne peut pas se poser seule
        public double LanderDeltaV;
        public double LanderGravity;
        public string LanderBody = "";
    }

    /// <summary>
    /// Budget Δv d'une mission par transferts de Hohmann entre orbites circulaires et coplanaires
    /// (coniques raccordées). C'est l'équivalent calculé d'une carte de Δv : la marge couvre
    /// les changements de plan, les corrections et le pilotage.
    /// </summary>
    public static class MissionBudget
    {
        // Δv sol → orbite basse de la carte communautaire, pour les corps du jeu de base non redimensionnés.
        static readonly Dictionary<string, double[]> StockAscent = new Dictionary<string, double[]>
        {
            // nom : { rayon (m), Δv (m/s) }
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

        /// <summary>Δv pour décoller et atteindre l'orbite basse.</summary>
        public static double Ascent(BodyInfo b)
        {
            double[] stock;
            if (StockAscent.TryGetValue(b.Key ?? b.Name, out stock) && Math.Abs(stock[0] - b.Radius) < 1) return stock[1];

            double v = b.LowOrbitSpeed;
            if (!b.HasAtmosphere) return v * 1.05;
            double rotation = b.RotationPeriod > 0 ? 2 * Math.PI * b.Radius / b.RotationPeriod : 0;
            // Pertes par gravité et par traînée, calées sur Kerbin (≈ 3 400 m/s).
            double gravityLoss = 0.3 * v;
            double dragLoss = 600 * Math.Pow(b.PressureAtm, 0.75);
            return v - rotation + gravityLoss + dragLoss;
        }

        /// <summary>Δv pour se poser depuis l'orbite basse. Avec une atmosphère, on compte sur l'aérofreinage et les parachutes.</summary>
        public static double Landing(BodyInfo b, out string note)
        {
            note = "";
            if (!b.HasAtmosphere) return Ascent(b);
            note = "aérofreinage et parachutes";
            return b.PressureAtm > 0.5 ? 0 : 0.15 * b.LowOrbitSpeed;
        }

        /// <summary>Δv entre une orbite basse et une vitesse d'excès hyperbolique vInf (éjection ou capture).</summary>
        public static double Hyperbolic(BodyInfo b, double vInf)
        {
            double r = b.LowOrbitRadius;
            return Math.Sqrt(vInf * vInf + 2 * b.Mu / r) - Math.Sqrt(b.Mu / r);
        }

        /// <summary>Vitesses d'excès au départ et à l'arrivée d'un Hohmann entre deux orbites circulaires autour de mu.</summary>
        public static void Hohmann(double mu, double r1, double r2, out double vDepart, out double vArrive)
        {
            double a = (r1 + r2) / 2;
            vDepart = Math.Abs(Math.Sqrt(mu * (2 / r1 - 1 / a)) - Math.Sqrt(mu / r1));
            vArrive = Math.Abs(Math.Sqrt(mu / r2) - Math.Sqrt(mu * (2 / r2 - 1 / a)));
        }

        /// <param name="payloadPhases">Étapes que le joueur confie à la charge utile (une case par étape),
        /// ou null pour la répartition automatique.</param>
        public static Budget Compute(BodyInfo home, BodyInfo dest, MissionType type, double margin, double payloadDeltaV,
            bool[] payloadPhases = null)
        {
            Budget budget = new Budget();
            bool landing = type == MissionType.Landing || type == MissionType.LandingReturn;
            bool back = type == MissionType.LandingReturn || type == MissionType.OrbitReturn;

            Add(budget, "Décollage et orbite basse de " + home.Name, Ascent(home), "");
            int arrival = 0;   // dernière étape du trajet jusqu'en orbite de la destination

            if (dest != home)
            {
                double outbound, capture, inbound, returnCapture;
                string capNote;
                if (!Transfer(home, dest, out outbound, out capture, out inbound, out returnCapture, out capNote))
                {
                    budget.Warnings.Add("Trajet entre " + home.Name + " et " + dest.Name + " non pris en charge : seul le décollage est compté.");
                }
                else
                {
                    Add(budget, "Injection vers " + dest.Name, outbound, "");
                    Add(budget, "Mise en orbite de " + dest.Name, capture, capNote);
                    arrival = budget.Phases.Count - 1;
                    if (landing) AddLanding(budget, dest);
                    if (landing && back) Add(budget, "Décollage de " + dest.Name, Ascent(dest), "");
                    if (back)
                    {
                        Add(budget, "Retour vers " + home.Name, inbound, "");
                        if (home.HasAtmosphere) Add(budget, "Rentrée sur " + home.Name, 0, "aérofreinage et parachutes");
                        else
                        {
                            Add(budget, "Mise en orbite de " + home.Name, returnCapture, "");
                            AddLanding(budget, home);
                        }
                    }
                }
            }
            else if (landing)
            {
                AddLanding(budget, home);
            }

            if (landing && !dest.HasSurface) budget.Warnings.Add(dest.Name + " n'a pas de surface solide : impossible de s'y poser.");

            foreach (Phase p in budget.Phases) budget.Total += p.DeltaV;
            budget.TotalWithMargin = budget.Total * (1 + margin);
            budget.PayloadDeltaV = Math.Max(0, payloadDeltaV);

            if (payloadPhases != null && payloadPhases.Length == budget.Phases.Count)
            {
                // Choix du joueur, étape par étape. Le décollage reste toujours au lanceur.
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
                    budget.Warnings.Add(string.Format("La charge utile n'a que {0:0} m/s pour les {1:0} m/s des étapes que tu lui confies.",
                        payloadDeltaV, payloadNeed));
            }
            else
            {
                // Le lanceur amène la charge utile en orbite de la destination ; le Δv de la charge utile sert à ce qu'elle
                // fait sur place (atterrissage, remontée, retour), en remontant depuis la fin, jamais au trajet.
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

            // Ce que la charge utile ne couvre pas à partir de l'atterrissage revient à un étage atterrisseur :
            // l'étage du haut d'un lanceur n'a ni jambes ni la poussée voulue pour se poser.
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
            Add(budget, "Atterrissage sur " + b.Name, dv, note);
            budget.Phases[budget.Phases.Count - 1].Landing = true;
        }

        static void Add(Budget budget, string name, double dv, string note)
        {
            budget.Phases.Add(new Phase { Name = name, DeltaV = dv, Note = note });
        }

        /// <summary>
        /// Calcule les Δv aller (éjection, capture) et retour (éjection, capture) entre l'orbite basse
        /// du corps de départ et celle de la destination. Cas pris en charge : lune du corps de départ,
        /// corps frère (même parent), lune d'un corps frère, et l'inverse (départ depuis une lune vers son parent).
        /// </summary>
        public static bool Transfer(BodyInfo home, BodyInfo dest, out double outbound, out double capture,
            out double inbound, out double returnCapture, out string captureNote)
        {
            outbound = capture = inbound = returnCapture = 0;
            captureNote = "";
            double vDep, vArr;

            if (dest.Parent == home)
            {
                // Lune du corps de départ : transfert depuis l'orbite basse jusqu'à l'orbite de la lune.
                Hohmann(home.Mu, home.LowOrbitRadius, dest.Sma, out vDep, out vArr);
                outbound = vDep;
                capture = Hyperbolic(dest, vArr);
                inbound = capture;
                returnCapture = vDep;
                return true;
            }

            if (home.Parent != null && dest == home.Parent)
            {
                // Depuis une lune vers son parent.
                Hohmann(dest.Mu, home.Sma, dest.LowOrbitRadius, out vDep, out vArr);
                outbound = Hyperbolic(home, vDep);
                capture = vArr;
                inbound = vArr;
                returnCapture = outbound;
                return true;
            }

            if (home.Parent != null && dest.Parent == home.Parent)
            {
                // Corps frères : transfert de Hohmann autour du parent commun.
                Hohmann(home.Parent.Mu, home.Sma, dest.Sma, out vDep, out vArr);
                outbound = Hyperbolic(home, vDep);
                capture = Hyperbolic(dest, vArr);
                inbound = capture;
                returnCapture = outbound;
                return true;
            }

            if (home.Parent != null && dest.Parent != null && dest.Parent.Parent == home.Parent)
            {
                // Lune d'une autre planète : transfert vers la planète, freinage jusqu'à l'orbite de la lune,
                // puis capture autour de la lune. Sans assistance gravitationnelle, donc prudent.
                BodyInfo planet = dest.Parent;
                Hohmann(home.Parent.Mu, home.Sma, planet.Sma, out vDep, out vArr);
                outbound = Hyperbolic(home, vDep);
                double r = dest.Sma;
                double planetBrake = Math.Sqrt(vArr * vArr + 2 * planet.Mu / r) - Math.Sqrt(planet.Mu / r);
                double moonCapture = Math.Sqrt(2 * dest.Mu / dest.LowOrbitRadius) - dest.LowOrbitSpeed;
                capture = planetBrake + moonCapture;
                if (planet.HasAtmosphere) captureNote = "aérocapture possible sur " + planet.Name;
                inbound = capture;
                returnCapture = outbound;
                return true;
            }

            return false;
        }
    }
}
