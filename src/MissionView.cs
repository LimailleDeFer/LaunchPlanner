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
        bool[] payloadPhases;         // étapes confiées à la charge utile ; null : répartition automatique
        string phasesKey = "";

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
            Budget b = MissionBudget.Compute(KspBodies.Home, destination, (MissionType)missionType, marginPercent / 100.0, PayloadDeltaV(),
                payloadPhases);
            // Une autre destination ou un autre type de mission change les étapes : on revient à la répartition automatique.
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
            Bold("Charge utile ?", GUILayout.Width(110));
            Bold("Assuré par", GUILayout.Width(200));
            GUILayout.EndHorizontal();

            bool[] toggled = null;
            phaseScroll = GUILayout.BeginScrollView(phaseScroll, GUILayout.Height(170));
            for (int i = 0; i < budget.Phases.Count; i++)
            {
                Phase p = budget.Phases[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(p.Name + (p.Note.Length > 0 ? " (" + p.Note + ")" : ""), GUILayout.Width(280));
                GUILayout.Label(p.DeltaV.ToString("0") + " m/s", GUILayout.Width(80));
                // Case : cette étape est faite par la charge utile (le décollage reste toujours au lanceur).
                if (i == 0) GUILayout.Label("", GUILayout.Width(110));
                else if (GUILayout.Toggle(p.CoveredByPayload, "", GUILayout.Width(110)) != p.CoveredByPayload)
                {
                    toggled = new bool[budget.Phases.Count];
                    for (int j = 0; j < toggled.Length; j++) toggled[j] = budget.Phases[j].CoveredByPayload;
                    toggled[i] = !p.CoveredByPayload;
                }
                string rest = p.LanderDeltaV > 0 ? "atterrisseur" : "lanceur";
                string who = p.CoveredByPayload ? "charge utile"
                    : p.CoveredDeltaV > 0 ? string.Format("charge utile ({0:0} m/s) + {1}", p.CoveredDeltaV, rest)
                    : rest;
                GUILayout.Label(who, GUILayout.Width(200));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUILayout.Label(payloadPhases == null
                ? "Répartition automatique : ta charge utile fait ce qui se passe sur place, selon son Δv. Coche ou décoche une étape pour choisir toi-même."
                : "Répartition choisie à la main.", GUILayout.Width(560));
            if (payloadPhases != null && GUILayout.Button("Automatique", GUILayout.Width(100))) { payloadPhases = null; budget = ComputeBudget(); }
            GUILayout.EndHorizontal();
            if (toggled != null)
            {
                payloadPhases = toggled;
                budget = ComputeBudget();
            }

            GUILayout.Label(string.Format("Total : {0:0} m/s, soit {1:0} m/s avec la marge de {2} %.",
                budget.Total, budget.TotalWithMargin, marginPercent));
            if (budget.PayloadDeltaV > 0)
                GUILayout.Label(budget.PayloadUsedDeltaV > 0.5 || payloadPhases != null
                    ? string.Format("La charge utile assure {0:0} m/s des étapes, sur les {1:0} m/s qu'elle a.", budget.PayloadUsedDeltaV, budget.PayloadDeltaV)
                    : string.Format("Les {0:0} m/s de la charge utile ne sont pas comptés : le lanceur l'amène jusqu'en orbite, elle les garde pour la suite.",
                        budget.PayloadDeltaV));
            if (budget.LanderDeltaV > 0)
                GUILayout.Label(string.Format("Ta charge utile ne peut pas se poser seule : un étage atterrisseur fournira {0:0} m/s sur {1}, " +
                    "avec des jambes et une poussée suffisante pour sa gravité.", budget.LanderDeltaV, budget.LanderBody));
            GUILayout.Label(string.Format("<size=16><b>Le lanceur doit fournir {0:0} m/s en emportant {1:0.00} t{2}.</b></size>",
                budget.LauncherDeltaV, payload.MassT, budget.LanderDeltaV > 0 ? " et l'atterrisseur" : ""), rich);

            foreach (string w in budget.Warnings) GUILayout.Label("<color=#ffbf33>" + w + "</color>", rich);
            GUILayout.Label("Estimation par transferts de Hohmann ; la marge couvre les changements de plan et les corrections.");
        }
    }
}
