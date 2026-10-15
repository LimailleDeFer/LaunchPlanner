using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;

namespace LaunchPlanner
{
    /// <summary>
    /// Onglet « Lanceurs » : à partir du budget de l'onglet Mission et de la masse de la charge utile,
    /// propose une variante par architecture (SSTO, deux étages, boosters à poudre, boosters liquides, asperges)
    /// et permet d'en choisir une.
    /// </summary>
    public class LauncherView
    {
        const float CardWidth = 300;

        readonly MissionView mission;

        // Réglages
        bool minimizeCost;
        double minLiftoffTwr = 1.3;
        bool manualMass;
        string manualMassText = "5";

        // Calcul en arrière-plan : le jeu ne se fige pas pendant la recherche.
        readonly object sync = new object();
        Thread worker;
        List<LauncherDesign> pending;
        string pendingError;
        string computedKey = "";
        string runningKey = "";

        List<LauncherDesign> designs = new List<LauncherDesign>();
        string error = "";
        double designsDeltaV;
        double designsMass;
        Vector2 scroll;

        /// <summary>La variante choisie, que l'étape 4 construira dans l'éditeur.</summary>
        public static LauncherDesign Chosen;

        GUIStyle rich;
        GUIStyle card;

        public LauncherView(MissionView mission)
        {
            this.mission = mission;
        }

        double PayloadMass()
        {
            if (!manualMass && mission.Payload.Present) return mission.Payload.MassT;
            double v;
            return double.TryParse(manualMassText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? Math.Max(0, v) : 0;
        }

        double TargetDeltaV()
        {
            return mission.Current != null ? mission.Current.LauncherDeltaV : 0;
        }

        string Key()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0}|{1:0.00}|{2}|{3:0.00}|{4}",
                TargetDeltaV(), PayloadMass(), minimizeCost, minLiftoffTwr, PartInventory.Parts.Count);
        }

        /// <summary>Appelé à chaque image tant que la fenêtre est ouverte : relance le calcul si la mission a changé.</summary>
        public void Update()
        {
            lock (sync)
            {
                if (pending != null || pendingError != null)
                {
                    designs = pending ?? new List<LauncherDesign>();
                    // Après un nouveau calcul, la variante choisie suit la mission.
                    if (Chosen != null)
                    {
                        LauncherDesign same = designs.Find(d => d.Variant == Chosen.Variant && d.Valid);
                        Chosen = same;
                    }
                    error = pendingError ?? "";
                    computedKey = runningKey;
                    pending = null;
                    pendingError = null;
                    worker = null;
                }
            }
            if (worker != null) return;
            string key = Key();
            if (key == computedKey || TargetDeltaV() <= 0) return;
            Start(key);
        }

        void Start(string key)
        {
            DesignRequest req = new DesignRequest
            {
                Parts = new List<PartInfo>(PartInventory.Parts),
                PayloadMass = PayloadMass(),
                TargetDeltaV = TargetDeltaV(),
                MinLiftoffTwr = minLiftoffTwr,
                MinimizeCost = minimizeCost,
            };
            BodyInfo home = KspBodies.Home;
            if (home != null)
            {
                req.HomeGravity = home.SurfaceGravity;
                req.HomeAtmosphere = home.HasAtmosphere;
                req.AscentDeltaV = MissionBudget.Ascent(home);
            }
            designsDeltaV = req.TargetDeltaV;
            designsMass = req.PayloadMass;
            runningKey = key;
            worker = new Thread(() =>
            {
                List<LauncherDesign> result = null;
                string problem = null;
                try
                {
                    result = new LauncherDesigner(req).Run();
                }
                catch (Exception e)
                {
                    problem = "Erreur pendant le calcul : " + e.Message;
                    Debug.LogException(e);
                }
                lock (sync)
                {
                    pending = result;
                    pendingError = problem;
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        void InitStyles()
        {
            if (rich != null) return;
            rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            card = new GUIStyle(GUI.skin.box) { padding = new RectOffset(8, 8, 6, 6) };
        }

        void Label(string text, params GUILayoutOption[] options)
        {
            GUILayout.Label(text, rich, options);
        }

        public void Draw()
        {
            InitStyles();
            DrawSettings();
            GUILayout.Space(6);

            if (TargetDeltaV() <= 0)
            {
                Label("Choisis d'abord une destination et une mission dans l'onglet « Mission ».");
                return;
            }
            if (error.Length > 0) Label("<color=#ff6666>" + error + "</color>");
            if (worker != null) Label("<i>Calcul des lanceurs en cours…</i>");
            if (designs.Count == 0) return;

            Label(string.Format("Variantes pour <b>{0:0} m/s</b> avec <b>{1:0.00} t</b> de charge utile, les étages listés du haut vers le bas :",
                designsDeltaV, designsMass));
            LauncherDesign best = Recommended();
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            GUILayout.BeginHorizontal();
            foreach (LauncherDesign d in designs) DrawCard(d, d == best);
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();

            if (Chosen != null)
                Label("Variante choisie : <b>" + Chosen.Variant + "</b>. La construction automatique dans l'éditeur arrive à l'étape 4.");
        }

        void DrawSettings()
        {
            GUILayout.BeginHorizontal();
            string target = mission.Destination != null ? mission.MissionName + " : " + mission.Destination.Name : "";
            Label(string.Format("<b>Objectif</b> {0}, le lanceur doit fournir <b>{1:0} m/s</b>", target, TargetDeltaV()));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (mission.Payload.Present)
            {
                manualMass = GUILayout.Toggle(manualMass, "Saisir la masse de la charge utile", GUILayout.Width(230));
                if (!manualMass) GUILayout.Label(string.Format("Charge utile : {0:0.00} t ({1})", mission.Payload.MassT,
                    mission.Payload.Name.Length > 0 ? mission.Payload.Name : "vaisseau ouvert"));
            }
            else GUILayout.Label("Pas de vaisseau dans l'éditeur, masse de la charge utile :", GUILayout.Width(330));
            if (manualMass || !mission.Payload.Present)
            {
                manualMassText = GUILayout.TextField(manualMassText, GUILayout.Width(60));
                GUILayout.Label("t");
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Optimiser :", GUILayout.Width(80));
            minimizeCost = GUILayout.Toolbar(minimizeCost ? 1 : 0, new[] { "la masse", "le coût" }, GUILayout.Width(200)) == 1;
            GUILayout.Space(20);
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "TWR minimal au décollage : {0:0.0}", minLiftoffTwr), GUILayout.Width(200));
            if (GUILayout.Button("−", GUILayout.Width(30))) minLiftoffTwr = Math.Max(1.1, Math.Round(minLiftoffTwr - 0.1, 1));
            if (GUILayout.Button("+", GUILayout.Width(30))) minLiftoffTwr = Math.Min(2.5, Math.Round(minLiftoffTwr + 0.1, 1));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Recalculer", GUILayout.Width(100))) computedKey = "";
            GUILayout.EndHorizontal();
        }

        LauncherDesign Recommended()
        {
            LauncherDesign best = null;
            foreach (LauncherDesign d in designs)
            {
                if (!d.Valid) continue;
                double score = minimizeCost ? d.Cost : d.TotalMass;
                if (best == null || score < (minimizeCost ? best.Cost : best.TotalMass)) best = d;
            }
            return best;
        }

        static string Money(double value)
        {
            return value.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');
        }

        void DrawCard(LauncherDesign d, bool recommended)
        {
            GUILayout.BeginVertical(card, GUILayout.Width(CardWidth));
            string badge = d == Chosen ? "  <color=#66ff66>(choisie)</color>" : recommended ? "  <color=#ffd34d>(recommandée)</color>" : "";
            Label("<size=15><b>" + d.Variant + "</b></size>" + badge);
            Label("<size=11>" + d.Description + "</size>");

            if (d.Stages.Count == 0)
            {
                Label("<color=#ffbf33>Impossible : " + d.Problem + "</color>");
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
                return;
            }
            if (!d.Valid) Label("<color=#ffbf33>Limite : " + d.Problem + "</color>");

            Label(string.Format("Masse au décollage <b>{0:0.0} t</b> (lanceur {1:0.0} t)", d.TotalMass, d.LauncherMass));
            Label(string.Format("Coût <b>{0}</b> · TWR au décollage <b>{1:0.00}</b>", Money(d.Cost), d.LiftoffTwr));
            string dvColor = d.DeltaV >= designsDeltaV - 1 ? "#66ff66" : "#ff6666";
            Label(string.Format("Δv <color={0}><b>{1:0} m/s</b></color> pour {2:0} demandés", dvColor, d.DeltaV, designsDeltaV));

            for (int i = d.Stages.Count - 1; i >= 0; i--)
            {
                StagePlan s = d.Stages[i];
                GUILayout.Space(4);
                string name = i == 0 ? (d.Stages.Count == 1 ? "Étage unique" : "Étage 1 (décollage)") : "Étage " + (i + 1);
                Label(string.Format("<b>{0}</b> : {1:0.0} t · coût {2} · Δv {3:0} m/s · TWR {4:0.00}", name, s.TotalMass, Money(s.Cost), s.DeltaV, s.Twr));
                Label("  " + s.EngineCount + " × " + s.Engine.Title);
                foreach (TankUse t in s.Tanks) Label("  " + t.Count + " × " + t.Tank.Title);
                if (s.Decoupler != null) Label("  1 × " + s.Decoupler.Title);
                BoosterPlan b = s.Boosters;
                if (b != null && b.Count > 0)
                {
                    string how = b.Solid ? "à poudre" : b.Crossfeed ? "liquides en asperges, largués 2 par 2" : "liquides";
                    Label(string.Format("  <b>+ {0} boosters {1}</b>, chacun :", b.Count, how));
                    Label("    1 × " + b.Part.Title);
                    if (!b.Solid) foreach (TankUse t in b.Tanks) Label("    " + t.Count + " × " + t.Tank.Title);
                    if (b.Decoupler != null) Label("    1 × " + b.Decoupler.Title);
                }
            }

            foreach (string note in d.Notes) Label("<color=#ffbf33>" + note + "</color>");
            GUILayout.FlexibleSpace();
            GUILayout.Space(4);
            if (d.Valid && GUILayout.Button(d == Chosen ? "Choisie" : "Choisir cette variante")) Chosen = d;
            GUILayout.EndVertical();
        }
    }
}
