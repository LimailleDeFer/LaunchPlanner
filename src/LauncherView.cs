using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;

namespace LaunchPlanner
{
    /// <summary>
    /// "Launchers" tab: from the Mission tab's budget and the payload mass,
    /// proposes one variant per architecture (SSTO, two stages, solid boosters, liquid boosters, asparagus)
    /// and lets the player pick one.
    /// </summary>
    public class LauncherView
    {
        const float CardWidth = 300;

        readonly MissionView mission;

        // Settings
        bool minimizeCost;
        double minLiftoffTwr = 1.3;
        bool manualMass;
        string manualMassText = "5";

        // Background computation: the game does not freeze during the search.
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

        /// <summary>The chosen variant, which step 4 will build in the editor.</summary>
        public static LauncherDesign Chosen;

        // Construction in the editor (step 4): requested from the UI, done in Update.
        LauncherDesign buildRequest;
        string buildMessage = "";
        readonly List<string> buildNotes = new List<string>();

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
            Budget b = mission.Current;
            return string.Format(CultureInfo.InvariantCulture, "{0:0}|{1:0.00}|{2}|{3:0.00}|{4}|{5:0.0}|{6}|{7:0}|{8:0.00}",
                TargetDeltaV(), PayloadMass(), minimizeCost, minLiftoffTwr, PartInventory.Parts.Count,
                mission.Payload.HeightM, mission.Payload.PartCount,
                b != null ? b.LanderDeltaV : 0, b != null ? b.LanderGravity : 0);
        }

        /// <summary>Called every frame while the window is open: restarts the computation if the mission changed.</summary>
        public void Update()
        {
            if (buildRequest != null)
            {
                LauncherDesign d = buildRequest;
                buildRequest = null;
                buildNotes.Clear();
                try
                {
                    string error = CraftSpawner.Spawn(d, buildNotes);
                    buildMessage = error != null ? "<color=#ff6666>" + error + "</color>"
                        : "<color=#66ff66>The launcher \"" + d.Variant + "\" is attached under your payload, stages included. " +
                          "To go back, load the vessel \"Before launcher\".</color>";
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    buildMessage = "<color=#ff6666>Error during construction: " + e.Message + "</color>";
                }
            }
            lock (sync)
            {
                if (pending != null || pendingError != null)
                {
                    designs = pending ?? new List<LauncherDesign>();
                    // After a new computation, the chosen variant follows the mission.
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
            SiteLimits.Apply(req);
            req.NoseCones = StructuralParts.NoseCones();
            req.Couplers = StructuralParts.MultiCouplers();
            if (!manualMass && mission.Payload.Present)
            {
                req.PayloadHeight = mission.Payload.HeightM;
                req.PayloadWidth = mission.Payload.WidthM;
                req.PayloadParts = mission.Payload.PartCount;
                req.PayloadBottomSize = StructuralParts.PayloadBottomSize();
            }
            Budget budget = mission.Current;
            if (budget != null && budget.LanderDeltaV > 0)
            {
                req.LanderDeltaV = budget.LanderDeltaV;
                req.LanderGravity = budget.LanderGravity;
                req.LanderBody = budget.LanderBody;
                req.Legs = LandingLegs.Unlocked();
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
                    problem = "Error during computation: " + e.Message;
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
                Label("First choose a destination and a mission in the \"Mission\" tab.");
                return;
            }
            if (error.Length > 0) Label("<color=#ff6666>" + error + "</color>");
            if (worker != null) Label("<i>Computing launchers...</i>");
            if (designs.Count == 0) return;

            Label(string.Format("Variants for <b>{0:0} m/s</b> with <b>{1:0.00} t</b> of payload, stages listed from top to bottom:",
                designsDeltaV, designsMass));
            if (SiteLimits.Describe.Length > 0)
                Label("<size=11>VAB and launch pad limits: " + SiteLimits.Describe + "</size>");
            LauncherDesign best = Recommended();
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            GUILayout.BeginHorizontal();
            foreach (LauncherDesign d in designs) DrawCard(d, d == best);
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();

            if (Chosen != null)
            {
                GUILayout.BeginHorizontal();
                Label("Chosen variant: <b>" + Chosen.Variant + "</b>", GUILayout.Width(300));
                if (GUILayout.Button("Build in the editor", GUILayout.Width(200))) buildRequest = Chosen;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            if (buildMessage.Length > 0) Label(buildMessage);
            foreach (string note in buildNotes) Label("<color=#ffbf33>" + note + "</color>");
        }

        void DrawSettings()
        {
            GUILayout.BeginHorizontal();
            string target = mission.Destination != null ? mission.MissionName + " : " + mission.Destination.Name : "";
            Label(string.Format("<b>Goal</b> {0}, the launcher must provide <b>{1:0} m/s</b>", target, TargetDeltaV()));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (mission.Payload.Present)
            {
                manualMass = GUILayout.Toggle(manualMass, "Enter the payload mass", GUILayout.Width(230));
                if (!manualMass) GUILayout.Label(string.Format("Payload: {0:0.00} t ({1})", mission.Payload.MassT,
                    mission.Payload.Name.Length > 0 ? mission.Payload.Name : "open vessel"));
            }
            else GUILayout.Label("No vessel in the editor, payload mass:", GUILayout.Width(330));
            if (manualMass || !mission.Payload.Present)
            {
                manualMassText = GUILayout.TextField(manualMassText, GUILayout.Width(60));
                GUILayout.Label("t");
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Optimize:", GUILayout.Width(80));
            minimizeCost = GUILayout.Toolbar(minimizeCost ? 1 : 0, new[] { "mass", "cost" }, GUILayout.Width(200)) == 1;
            GUILayout.Space(20);
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "Minimum liftoff TWR: {0:0.0}", minLiftoffTwr), GUILayout.Width(200));
            if (GUILayout.Button("−", GUILayout.Width(30))) minLiftoffTwr = Math.Max(1.1, Math.Round(minLiftoffTwr - 0.1, 1));
            if (GUILayout.Button("+", GUILayout.Width(30))) minLiftoffTwr = Math.Min(2.5, Math.Round(minLiftoffTwr + 0.1, 1));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Recompute", GUILayout.Width(100))) computedKey = "";
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
            return value.ToString("#,0", CultureInfo.InvariantCulture);
        }

        void DrawCard(LauncherDesign d, bool recommended)
        {
            GUILayout.BeginVertical(card, GUILayout.Width(CardWidth));
            string badge = d == Chosen ? "  <color=#66ff66>(chosen)</color>" : recommended ? "  <color=#ffd34d>(recommended)</color>" : "";
            Label("<size=15><b>" + d.Variant + "</b></size>" + badge);
            Label("<size=11>" + d.Description + "</size>");

            if (d.Stages.Count == 0)
            {
                Label("<color=#ffbf33>Impossible: " + d.Problem + "</color>");
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
                return;
            }
            if (!d.Valid) Label("<color=#ffbf33>Limit: " + d.Problem + "</color>");

            Label(string.Format("Liftoff mass <b>{0:0.0} t</b> (launcher {1:0.0} t)", d.TotalMass, d.LauncherMass));
            Label(string.Format("Cost <b>{0}</b> · Liftoff TWR <b>{1:0.00}</b>", Money(d.Cost), d.LiftoffTwr));
            Label(string.Format(CultureInfo.InvariantCulture, "Height ≈ <b>{0:0.#} m</b> · width ≈ {1:0.#} m · {2} parts (payload included)", d.HeightM, d.WidthM, d.PartCount));
            string dvColor = d.DeltaV >= designsDeltaV - 1 ? "#66ff66" : "#ff6666";
            Label(string.Format("Δv <color={0}><b>{1:0} m/s</b></color> for {2:0} required", dvColor, d.DeltaV, designsDeltaV));

            if (d.Lander != null)
            {
                StagePlan l = d.Lander;
                Label(string.Format("<b>Lander</b>: {0:0.0} t · Δv {1:0} m/s · TWR {2:0.00} on site",
                    l.TotalMass + d.LanderLegCount * (d.LanderLegs != null ? d.LanderLegs.DryMassT : 0), l.DeltaV, l.Twr));
                Label("  Ø " + LauncherDesigner.Diameter(l.Size) + " · " + l.EngineCount + " × " + l.Engine.Title);
                foreach (TankUse t in l.Tanks) Label("  " + t.Count + " × " + t.Tank.Title);
                if (d.LanderLegs != null) Label("  " + d.LanderLegCount + " × " + d.LanderLegs.Title);
            }
            if (d.PayloadDecoupler != null) Label("<size=11>" + (d.Lander != null ? "Under the lander" : "Under the payload") +
                ": 1 × " + d.PayloadDecoupler.Title + "</size>");
            for (int i = d.Stages.Count - 1; i >= 0; i--)
            {
                StagePlan s = d.Stages[i];
                GUILayout.Space(4);
                string name = i == 0 ? (d.Stages.Count == 1 ? "Single stage" : "Stage 1 (liftoff)") : "Stage " + (i + 1);
                Label(string.Format("<b>{0}</b>: {1:0.0} t · cost {2} · Δv {3:0} m/s · TWR {4:0.00}", name, s.TotalMass, Money(s.Cost), s.DeltaV, s.Twr));
                Label("  Ø " + LauncherDesigner.Diameter(s.Size) + " · " + s.EngineCount + " × " + s.Engine.Title);
                foreach (TankUse t in s.Tanks) Label("  " + t.Count + " × " + t.Tank.Title);
                if (s.Coupler != null) Label("  1 × " + s.Coupler.Title);
                if (s.Decoupler != null) Label("  1 × " + s.Decoupler.Title);
                BoosterPlan b = s.Boosters;
                if (b != null && b.Count > 0)
                {
                    string how = b.Solid ? "solid" : b.Crossfeed ? "liquid asparagus, dropped 2 by 2" : "liquid";
                    Label(string.Format("  <b>+ {0} {1} boosters</b>, each:", b.Count, how));
                    Label("    " + (b.Solid ? 1 : b.EnginesEach) + " × " + b.Part.Title);
                    if (b.Coupler != null) Label("    1 × " + b.Coupler.Title);
                    if (!b.Solid) foreach (TankUse t in b.Tanks) Label("    " + t.Count + " × " + t.Tank.Title);
                    if (b.Decoupler != null) Label("    1 × " + b.Decoupler.Title);
                    if (b.Separator != null && b.SeparatorsEach > 0) Label("    " + b.SeparatorsEach + " × " + b.Separator.Title);
                    if (b.NoseCone != null) Label("    1 × " + b.NoseCone.Title);
                }
            }

            foreach (string note in d.Notes) Label("<color=#ffbf33>" + note + "</color>");
            GUILayout.FlexibleSpace();
            GUILayout.Space(4);
            if (d.Valid && GUILayout.Button(d == Chosen ? "Chosen" : "Choose this variant")) Chosen = d;
            GUILayout.EndVertical();
        }
    }
}
