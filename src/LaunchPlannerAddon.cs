using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LaunchPlanner
{
    /// <summary>Fenêtre du planificateur dans l'éditeur (VAB et SPH), ouverte par un bouton de la barre d'outils.</summary>
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class LaunchPlannerAddon : MonoBehaviour
    {
        const int WindowId = 0x4C504C31;
        static readonly string[] Tabs = { "Moteurs", "Boosters", "Réservoirs", "Découpleurs" };

        ApplicationLauncherButton button;
        bool buttonFailed;
        bool visible;
        Rect windowRect = new Rect(120, 80, 860, 560);
        Vector2 scroll;
        int tab;
        bool showLocked;
        string status = "";

        void Start()
        {
            try
            {
                PartInventory.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                status = "Erreur à la lecture des pièces : " + e.Message;
            }
        }

        void Update()
        {
            if (button != null || buttonFailed) return;
            try
            {
                TryAddButton();
            }
            catch (Exception e)
            {
                // Si la barre d'outils n'est pas accessible, on affiche quand même la fenêtre.
                buttonFailed = true;
                visible = true;
                Debug.LogException(e);
            }
        }

        void TryAddButton()
        {
            ApplicationLauncher launcher = Refl.GetStatic(typeof(ApplicationLauncher), "Instance") as ApplicationLauncher;
            object ready = Refl.GetStatic(typeof(ApplicationLauncher), "Ready");
            if (launcher == null || !(ready is bool) || !(bool)ready) return;
            button = launcher.AddModApplication(
                () => visible = true, () => visible = false, null, null, null, null,
                ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH, MakeIcon());
        }

        void OnDestroy()
        {
            if (button == null) return;
            try
            {
                ApplicationLauncher launcher = Refl.GetStatic(typeof(ApplicationLauncher), "Instance") as ApplicationLauncher;
                if (launcher != null) launcher.RemoveModApplication(button);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void OnGUI()
        {
            if (!visible) return;
            windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow, "Planificateur de lanceur — pièces disponibles");
        }

        void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            int unlocked = 0;
            SortedDictionary<string, int> sources = new SortedDictionary<string, int>();
            foreach (PartInfo p in PartInventory.Parts)
            {
                if (!p.Unlocked) continue;
                unlocked++;
                int n;
                sources.TryGetValue(p.Source, out n);
                sources[p.Source] = n + 1;
            }
            GUILayout.Label(string.Format("{0} pièces chargées, {1} utiles au lanceur dont {2} débloquées.",
                PartInventory.LoadedCount, PartInventory.Parts.Count, unlocked));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Relire", GUILayout.Width(80))) PartInventory.Refresh();
            if (GUILayout.Button("Exporter CSV", GUILayout.Width(110))) Export();
            if (buttonFailed && GUILayout.Button("Fermer", GUILayout.Width(70))) visible = false;
            GUILayout.EndHorizontal();

            List<string> bySource = new List<string>();
            foreach (KeyValuePair<string, int> kv in sources) bySource.Add(kv.Key + " (" + kv.Value + ")");
            GUILayout.Label("Origine : " + string.Join(", ", bySource.ToArray()));

            GUILayout.BeginHorizontal();
            tab = GUILayout.Toolbar(tab, Tabs);
            showLocked = GUILayout.Toggle(showLocked, "Afficher aussi les pièces non débloquées", GUILayout.Width(300));
            GUILayout.EndHorizontal();

            PartKind kind = (PartKind)tab;
            DrawHeader(kind);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(400));
            foreach (PartInfo p in PartInventory.Parts)
            {
                if (p.Kind != kind || (!p.Unlocked && !showLocked)) continue;
                DrawRow(p);
            }
            GUILayout.EndScrollView();

            if (status.Length > 0) GUILayout.Label(status);
            foreach (string err in PartInventory.Errors) GUILayout.Label("Erreur : " + err);
            GUI.DragWindow();
        }

        static void DrawHeader(PartKind kind)
        {
            GUILayout.BeginHorizontal();
            Cell("Pièce", 240);
            Cell("Origine", 110);
            Cell("Taille", 90);
            switch (kind)
            {
                case PartKind.Engine:
                    Cell("Poussée sol/vide (kN)", 140);
                    Cell("Isp sol/vide (s)", 100);
                    Cell("Masse (t)", 70);
                    Cell("Ergols", 90);
                    break;
                case PartKind.Booster:
                    Cell("Poussée sol/vide (kN)", 140);
                    Cell("Isp sol/vide (s)", 100);
                    Cell("Masse vide/pleine (t)", 130);
                    Cell("Durée (s)", 60);
                    break;
                case PartKind.Tank:
                    Cell("Masse vide/pleine (t)", 130);
                    Cell("Contenu", 260);
                    break;
                default:
                    Cell("Masse (t)", 70);
                    break;
            }
            GUILayout.EndHorizontal();
        }

        static void DrawRow(PartInfo p)
        {
            GUILayout.BeginHorizontal();
            Cell((p.Unlocked ? "" : "[verrouillée] ") + p.Title, 240);
            Cell(p.Source, 110);
            Cell(p.Sizes, 90);
            switch (p.Kind)
            {
                case PartKind.Engine:
                    Cell(string.Format("{0:0} / {1:0}", p.ThrustAslKN, p.ThrustVacKN), 140);
                    Cell(string.Format("{0:0} / {1:0}", p.IspAsl, p.IspVac), 100);
                    Cell(p.DryMassT.ToString("0.###"), 70);
                    Cell(p.Propellants, 90);
                    break;
                case PartKind.Booster:
                    Cell(string.Format("{0:0} / {1:0}", p.ThrustAslKN, p.ThrustVacKN), 140);
                    Cell(string.Format("{0:0} / {1:0}", p.IspAsl, p.IspVac), 100);
                    Cell(string.Format("{0:0.###} / {1:0.###}", p.DryMassT, p.WetMassT), 130);
                    Cell(p.BurnTimeS.ToString("0"), 60);
                    break;
                case PartKind.Tank:
                    Cell(string.Format("{0:0.###} / {1:0.###}", p.DryMassT, p.WetMassT), 130);
                    List<string> res = new List<string>();
                    foreach (ResourceAmount r in p.Resources) res.Add(r.Name + " " + r.Amount.ToString("0"));
                    Cell(string.Join(", ", res.ToArray()), 260);
                    break;
                default:
                    Cell(p.DryMassT.ToString("0.###"), 70);
                    break;
            }
            GUILayout.EndHorizontal();
        }

        static void Cell(string text, float width)
        {
            GUILayout.Label(text, GUILayout.Width(width));
        }

        void Export()
        {
            try
            {
                // Plugins/LaunchPlanner.dll -> GameData/LaunchPlanner/inventaire.csv
                string dir = Path.GetDirectoryName(Path.GetDirectoryName(typeof(LaunchPlannerAddon).Assembly.Location));
                status = "Inventaire exporté : " + PartInventory.ExportCsv(dir);
            }
            catch (Exception e)
            {
                status = "Export impossible : " + e.Message;
                Debug.LogException(e);
            }
        }

        static Texture2D MakeIcon()
        {
            // Icône 38x38 dessinée à la main : une fusée blanche sur fond transparent.
            const int size = 38;
            Texture2D tex = new Texture2D(size, size);
            Color clear = new Color(0, 0, 0, 0);
            Color white = new Color(1, 1, 1, 1);
            Color flame = new Color(1f, 0.6f, 0.1f, 1);
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    Color c = clear;
                    int dx = Math.Abs(x - size / 2);
                    if (y >= 10 && y <= 28 && dx <= 4) c = white;                 // corps
                    else if (y > 28 && y <= 34 && dx <= 34 - y) c = white;        // coiffe
                    else if (y >= 8 && y <= 14 && dx > 4 && dx <= 8) c = white;   // ailerons
                    else if (y >= 3 && y < 10 && dx <= (y - 3) / 2) c = flame;    // flamme
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            return tex;
        }
    }
}
