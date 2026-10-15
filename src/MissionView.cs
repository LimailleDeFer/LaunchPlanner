using System;
using System.Globalization;
using UnityEngine;

namespace LaunchPlanner
{
    /// <summary>Onglet « Mission » : destination, type de mission, charge utile et budget Δv.</summary>
    public class MissionView
    {
        static readonly string[] MissionTypes = { "Orbite", "Atterrissage", "Atterrissage + retour", "Orbite + retour" };

        BodyInfo destination;
        int missionType = 1;
        int marginPercent = 10;
        Vector2 bodyScroll;
        Vector2 phaseScroll;
        Payload payload = new Payload();
        bool manualDeltaV;
        string manualDeltaVText = "0";
        float nextPayloadRead;
        int launcherLastPhase = -1;   // -1 : selon le Δv de la charge utile

        GUIStyle rich;

        /// <summary>Dernier budget calculé, lu par l'onglet « Lanceurs ».</summary>
        public Budget Current { get; private set; }
        public BodyInfo Destination { get { return destination; } }
        public string MissionName { get { return MissionTypes[missionType]; } }
        public Payload Payload { get { return payload; } }

        public void Refresh()
        {
            KspBodies.Refresh();
            if (destination == null || !KspBodies.All.Contains(destination))
            {
                // Par défaut : la première lune du corps de départ.
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
            return Current = MissionBudget.Compute(KspBodies.Home, destination, (MissionType)missionType, marginPercent / 100.0, PayloadDeltaV(),
                launcherLastPhase);
        }

        void Bold(string text, params GUILayoutOption[] options)
        {
            GUILayout.Label("<b>" + text + "</b>", rich, options);
        }

        void InitStyles()
        {
            if (rich != null) return;
            // Texte enrichi plutôt que FontStyle, qui obligerait à référencer un module Unity de plus.
            rich = new GUIStyle(GUI.skin.label) { richText = true };
        }

        public void Draw()
        {
            InitStyles();
            if (KspBodies.Home == null)
            {
                GUILayout.Label("Corps célestes non disponibles.");
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
                string label = new string(' ', Math.Max(0, b.Depth - 1) * 4) + b.Name + (b == KspBodies.Home ? " (départ)" : "");
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
            GUILayout.Label("Marge de sécurité : " + marginPercent + " %", GUILayout.Width(200));
            if (GUILayout.Button("−", GUILayout.Width(30))) marginPercent = Math.Max(0, marginPercent - 5);
            if (GUILayout.Button("+", GUILayout.Width(30))) marginPercent = Math.Min(100, marginPercent + 5);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        void DrawPayload()
        {
            Bold("Charge utile : le vaisseau ouvert dans l'éditeur");
            if (!payload.Present)
            {
                GUILayout.Label("Aucun vaisseau dans l'éditeur. Charge ton atterrisseur ou ton module (bouton Charger, ou en sous-assemblage).");
                return;
            }
            GUILayout.Label(string.Format("{0} : {1} pièces, {2:0.00} t au total dont {3:0.00} t à vide",
                payload.Name.Length > 0 ? payload.Name : "Sans nom", payload.PartCount, payload.MassT, payload.DryMassT));

            GUILayout.BeginHorizontal();
            string auto = payload.DeltaVKnown ? payload.DeltaVVac.ToString("0") + " m/s (calcul du jeu, dans le vide)" : "non disponible";
            manualDeltaV = GUILayout.Toggle(manualDeltaV, "Saisir le Δv de la charge utile", GUILayout.Width(230));
            if (manualDeltaV)
            {
                manualDeltaVText = GUILayout.TextField(manualDeltaVText, GUILayout.Width(70));
                GUILayout.Label("m/s");
            }
            else GUILayout.Label("Δv propre : " + auto);
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

            Bold("Budget Δv : " + KspBodies.Home.Name + " → " + destination.Name);
            GUILayout.BeginHorizontal();
            Bold("Étape", GUILayout.Width(280));
            Bold("Δv", GUILayout.Width(80));
            Bold("Assuré par", GUILayout.Width(200));
            GUILayout.EndHorizontal();

            phaseScroll = GUILayout.BeginScrollView(phaseScroll, GUILayout.Height(170));
            foreach (Phase p in budget.Phases)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(p.Name + (p.Note.Length > 0 ? " (" + p.Note + ")" : ""), GUILayout.Width(280));
                GUILayout.Label(p.DeltaV.ToString("0") + " m/s", GUILayout.Width(80));
                string who = p.CoveredByPayload ? "charge utile"
                    : p.CoveredDeltaV > 0 ? string.Format("charge utile ({0:0} m/s) + lanceur", p.CoveredDeltaV)
                    : "lanceur";
                GUILayout.Label(who, GUILayout.Width(200));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            // Jusqu'où le lanceur emmène la charge utile (par exemple : un atterrisseur qui fait lui-même la suite).
            GUILayout.Label("Le lanceur emmène la charge utile jusqu'à :");
            string[] choices = new string[budget.Phases.Count + 1];
            choices[0] = "Automatique (selon son Δv)";
            for (int i = 0; i < budget.Phases.Count; i++) choices[i + 1] = "Fin de : " + budget.Phases[i].Name;
            if (launcherLastPhase >= budget.Phases.Count) launcherLastPhase = -1;
            int choice = GUILayout.SelectionGrid(launcherLastPhase + 1, choices, 2);
            if (choice != launcherLastPhase + 1)
            {
                launcherLastPhase = choice - 1;
                budget = ComputeBudget();
            }

            GUILayout.Label(string.Format("Total : {0:0} m/s, soit {1:0} m/s avec la marge de {2} %.",
                budget.Total, budget.TotalWithMargin, marginPercent));
            if (budget.PayloadDeltaV > 0)
                GUILayout.Label(string.Format("La charge utile en assure {0:0} m/s.",
                    Math.Min(budget.PayloadDeltaV, budget.TotalWithMargin - budget.LauncherDeltaV)));
            GUILayout.Label(string.Format("<size=16><b>Le lanceur doit fournir {0:0} m/s en emportant {1:0.00} t.</b></size>",
                budget.LauncherDeltaV, payload.MassT), rich);

            foreach (string w in budget.Warnings) GUILayout.Label("<color=#ffbf33>" + w + "</color>", rich);
            GUILayout.Label("Estimation par transferts de Hohmann ; la marge couvre les changements de plan et les corrections.");
        }
    }
}
