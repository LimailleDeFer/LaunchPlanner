using System;
using System.Globalization;
using UnityEngine;

namespace LaunchPlanner
{
    /// <summary>"Mission" tab: destination, mission type, payload and delta-v budget.</summary>
    public class MissionView
    {
        static readonly string[] MissionTypes = { "Orbit", "Landing", "Landing + return", "Orbit + return" };

        BodyInfo destination;
        int missionType = 1;
        int marginPercent = 10;
        Vector2 bodyScroll;
        Vector2 phaseScroll;
        Payload payload = new Payload();
        bool manualDeltaV;
        string manualDeltaVText = "0";
        float nextPayloadRead;
        bool[] payloadPhases;         // phases assigned to the payload; null: automatic split
        string phasesKey = "";

        GUIStyle rich;

        /// <summary>Last computed budget, read by the "Launchers" tab.</summary>
        public Budget Current { get; private set; }
        public BodyInfo Destination { get { return destination; } }
        public string MissionName { get { return MissionTypes[missionType]; } }
        public Payload Payload { get { return payload; } }

        public void Refresh()
        {
            KspBodies.Refresh();
            if (destination == null || !KspBodies.All.Contains(destination))
            {
                // By default: the first moon of the home body.
                destination = KspBodies.Home;
                foreach (BodyInfo b in KspBodies.All)
                    if (b.Parent == KspBodies.Home) { destination = b; break; }
            }
        }

        public void Update()
        {
            if (Time.realtimeSinceStartup < nextPayloadRead) return;
            nextPayloadRead = Time.realtimeSinceStartup + 1f;
            payload = Payload.FromEditor();
            ComputeBudget();
        }

        Budget ComputeBudget()
        {
            if (destination == null || KspBodies.Home == null) return Current = null;
            Budget b = MissionBudget.Compute(KspBodies.Home, destination, (MissionType)missionType, marginPercent / 100.0, PayloadDeltaV(),
                payloadPhases);
            // Another destination or mission type changes the phases: back to the automatic split.
            string key = string.Join("|", b.Phases.ConvertAll(p => p.Name).ToArray());
            if (key != phasesKey)
            {
                phasesKey = key;
                if (payloadPhases != null)
                {
                    payloadPhases = null;
                    b = MissionBudget.Compute(KspBodies.Home, destination, (MissionType)missionType, marginPercent / 100.0, PayloadDeltaV());
                }
            }
            return Current = b;
        }

        void Bold(string text, params GUILayoutOption[] options)
        {
            GUILayout.Label("<b>" + text + "</b>", rich, options);
        }

        void InitStyles()
        {
            if (rich != null) return;
            // Rich text rather than FontStyle, which would require referencing one more Unity module.
            rich = new GUIStyle(GUI.skin.label) { richText = true };
        }

        public void Draw()
        {
            InitStyles();
            if (KspBodies.Home == null)
            {
                GUILayout.Label("Celestial bodies not available.");
                return;
            }

            GUILayout.BeginHorizontal();
            DrawBodies();
            GUILayout.Space(10);
            GUILayout.BeginVertical();
            DrawSettings();
            GUILayout.Space(6);
            DrawPayload();
            GUILayout.Space(6);
            DrawBudget();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        void DrawBodies()
        {
            GUILayout.BeginVertical(GUILayout.Width(200));
            Bold("Destination");
            bodyScroll = GUILayout.BeginScrollView(bodyScroll, GUILayout.Width(200), GUILayout.Height(400));
            foreach (BodyInfo b in KspBodies.All)
            {
                if (b.IsStar) continue;
                string label = new string(' ', Math.Max(0, b.Depth - 1) * 4) + b.Name + (b == KspBodies.Home ? " (home)" : "");
                bool selected = b == destination;
                if (GUILayout.Toggle(selected, label) && !selected) destination = b;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        void DrawSettings()
        {
            Bold("Mission");
            missionType = GUILayout.Toolbar(missionType, MissionTypes);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Safety margin: " + marginPercent + " %", GUILayout.Width(200));
            if (GUILayout.Button("−", GUILayout.Width(30))) marginPercent = Math.Max(0, marginPercent - 5);
            if (GUILayout.Button("+", GUILayout.Width(30))) marginPercent = Math.Min(100, marginPercent + 5);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        void DrawPayload()
        {
            Bold("Payload: the vessel open in the editor");
            if (!payload.Present)
            {
                GUILayout.Label("No vessel in the editor. Load your lander or module (Load button, or as a subassembly).");
                return;
            }
            GUILayout.Label(string.Format("{0}: {1} parts, {2:0.00} t total of which {3:0.00} t dry",
                payload.Name.Length > 0 ? payload.Name : "Unnamed", payload.PartCount, payload.MassT, payload.DryMassT));

            GUILayout.BeginHorizontal();
            string auto = payload.DeltaVKnown ? payload.DeltaVVac.ToString("0") + " m/s (game calculation, in vacuum)" : "not available";
            manualDeltaV = GUILayout.Toggle(manualDeltaV, "Enter the payload delta-v", GUILayout.Width(230));
            if (manualDeltaV)
            {
                manualDeltaVText = GUILayout.TextField(manualDeltaVText, GUILayout.Width(70));
                GUILayout.Label("m/s");
            }
            else GUILayout.Label("Own delta-v: " + auto);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        double PayloadDeltaV()
        {
            if (!payload.Present) return 0;
            if (!manualDeltaV) return payload.DeltaVKnown ? payload.DeltaVVac : 0;
            double v;
            return double.TryParse(manualDeltaVText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        void DrawBudget()
        {
            if (destination == null) return;
            Budget budget = ComputeBudget();

            Bold("Delta-v budget: " + KspBodies.Home.Name + " → " + destination.Name);
            GUILayout.BeginHorizontal();
            Bold("Phase", GUILayout.Width(280));
            Bold("Δv", GUILayout.Width(80));
            Bold("Payload?", GUILayout.Width(110));
            Bold("Handled by", GUILayout.Width(200));
            GUILayout.EndHorizontal();

            bool[] toggled = null;
            phaseScroll = GUILayout.BeginScrollView(phaseScroll, GUILayout.Height(170));
            for (int i = 0; i < budget.Phases.Count; i++)
            {
                Phase p = budget.Phases[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(p.Name + (p.Note.Length > 0 ? " (" + p.Note + ")" : ""), GUILayout.Width(280));
                GUILayout.Label(p.DeltaV.ToString("0") + " m/s", GUILayout.Width(80));
                // Checkbox: this phase is done by the payload (liftoff always stays with the launcher).
                if (i == 0) GUILayout.Label("", GUILayout.Width(110));
                else if (GUILayout.Toggle(p.CoveredByPayload, "", GUILayout.Width(110)) != p.CoveredByPayload)
                {
                    toggled = new bool[budget.Phases.Count];
                    for (int j = 0; j < toggled.Length; j++) toggled[j] = budget.Phases[j].CoveredByPayload;
                    toggled[i] = !p.CoveredByPayload;
                }
                string rest = p.LanderDeltaV > 0 ? "lander" : "launcher";
                string who = p.CoveredByPayload ? "payload"
                    : p.CoveredDeltaV > 0 ? string.Format("payload ({0:0} m/s) + {1}", p.CoveredDeltaV, rest)
                    : rest;
                GUILayout.Label(who, GUILayout.Width(200));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUILayout.Label(payloadPhases == null
                ? "Automatic split: your payload does what happens on site, according to its delta-v. Check or uncheck a phase to choose yourself."
                : "Split chosen manually.", GUILayout.Width(560));
            if (payloadPhases != null && GUILayout.Button("Automatic", GUILayout.Width(100))) { payloadPhases = null; budget = ComputeBudget(); }
            GUILayout.EndHorizontal();
            if (toggled != null)
            {
                payloadPhases = toggled;
                budget = ComputeBudget();
            }

            GUILayout.Label(string.Format("Total: {0:0} m/s, or {1:0} m/s with the {2} % margin.",
                budget.Total, budget.TotalWithMargin, marginPercent));
            if (budget.PayloadDeltaV > 0)
                GUILayout.Label(budget.PayloadUsedDeltaV > 0.5 || payloadPhases != null
                    ? string.Format("The payload handles {0:0} m/s of the phases, out of the {1:0} m/s it has.", budget.PayloadUsedDeltaV, budget.PayloadDeltaV)
                    : string.Format("The payload's {0:0} m/s are not counted: the launcher takes it to orbit, and it keeps them for later.",
                        budget.PayloadDeltaV));
            if (budget.LanderDeltaV > 0)
                GUILayout.Label(string.Format("Your payload cannot land by itself: a lander stage will provide {0:0} m/s at {1}, " +
                    "with legs and enough thrust for its gravity.", budget.LanderDeltaV, budget.LanderBody));
            GUILayout.Label(string.Format("<size=16><b>The launcher must provide {0:0} m/s while carrying {1:0.00} t{2}.</b></size>",
                budget.LauncherDeltaV, payload.MassT, budget.LanderDeltaV > 0 ? " and the lander" : ""), rich);

            foreach (string w in budget.Warnings) GUILayout.Label("<color=#ffbf33>" + w + "</color>", rich);
            GUILayout.Label("Estimate using Hohmann transfers; the margin covers plane changes and corrections.");
        }
    }
}
