using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LaunchPlanner
{
    /// <summary>"Parts" tab: inventory of the parts read, for verification.</summary>
    public class PartsView
    {
        static readonly string[] Tabs = { "Engines", "Boosters", "Tanks", "Decouplers" };

        Vector2 scroll;
        int tab;
        bool showLocked;
        string status = "";

        public void ReportError(string message)
        {
            status = message;
        }

        public void Draw()
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
            GUILayout.Label(string.Format("{0} parts loaded, {1} useful to the launcher, {2} of them unlocked.",
                PartInventory.LoadedCount, PartInventory.Parts.Count, unlocked));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reload", GUILayout.Width(80))) PartInventory.Refresh();
            if (GUILayout.Button("Export CSV", GUILayout.Width(110))) Export();
            GUILayout.EndHorizontal();

            List<string> bySource = new List<string>();
            foreach (KeyValuePair<string, int> kv in sources) bySource.Add(kv.Key + " (" + kv.Value + ")");
            GUILayout.Label("Source: " + string.Join(", ", bySource.ToArray()));

            GUILayout.BeginHorizontal();
            tab = GUILayout.Toolbar(tab, Tabs);
            showLocked = GUILayout.Toggle(showLocked, "Also show locked parts", GUILayout.Width(300));
            GUILayout.EndHorizontal();

            PartKind kind = (PartKind)tab;
            DrawHeader(kind);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(380));
            foreach (PartInfo p in PartInventory.Parts)
            {
                if (p.Kind != kind || (!p.Unlocked && !showLocked)) continue;
                DrawRow(p);
            }
            GUILayout.EndScrollView();

            if (status.Length > 0) GUILayout.Label(status);
            foreach (string err in PartInventory.Errors) GUILayout.Label("Error: " + err);
        }

        static void DrawHeader(PartKind kind)
        {
            GUILayout.BeginHorizontal();
            Cell("Part", 240);
            Cell("Source", 110);
            Cell("Size", 90);
            switch (kind)
            {
                case PartKind.Engine:
                    Cell("Thrust ASL/vac (kN)", 140);
                    Cell("Isp ASL/vac (s)", 100);
                    Cell("Mass (t)", 70);
                    Cell("Propellants", 90);
                    break;
                case PartKind.Booster:
                    Cell("Thrust ASL/vac (kN)", 140);
                    Cell("Isp ASL/vac (s)", 100);
                    Cell("Dry/wet mass (t)", 130);
                    Cell("Burn (s)", 60);
                    break;
                case PartKind.Tank:
                    Cell("Dry/wet mass (t)", 130);
                    Cell("Contents", 260);
                    break;
                default:
                    Cell("Mass (t)", 70);
                    break;
            }
            GUILayout.EndHorizontal();
        }

        static void DrawRow(PartInfo p)
        {
            GUILayout.BeginHorizontal();
            Cell((p.Unlocked ? "" : "[locked] ") + p.Title, 240);
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
                // Plugins/LaunchPlanner.dll -> GameData/LaunchPlanner/inventory.csv
                string dir = Path.GetDirectoryName(Path.GetDirectoryName(typeof(PartsView).Assembly.Location));
                status = "Inventory exported: " + PartInventory.ExportCsv(dir);
            }
            catch (Exception e)
            {
                status = "Export failed: " + e.Message;
                Debug.LogException(e);
            }
        }

    }
}
