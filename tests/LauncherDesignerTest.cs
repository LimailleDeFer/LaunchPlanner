// Lance le calcul des lanceurs sur un inventaire exporté du jeu (bouton « Exporter CSV »).
//   mcs tests/LauncherDesignerTest.cs src/LauncherDesigner.cs src/PartInfo.cs -out:build/designer.exe
//   mono build/designer.exe inventaire.csv [masse charge utile t] [Δv m/s]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using LaunchPlanner;

static class LauncherDesignerTest
{
    static double D(string s)
    {
        double v;
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        return v;
    }

    static List<PartInfo> Load(string path)
    {
        List<PartInfo> parts = new List<PartInfo>();
        string[] lines = File.ReadAllLines(path);
        string[] head = lines[0].TrimStart('﻿').Split(';');
        Dictionary<string, int> col = new Dictionary<string, int>();
        for (int i = 0; i < head.Length; i++) col[head[i]] = i;
        for (int l = 1; l < lines.Length; l++)
        {
            string[] c = lines[l].Split(';');
            if (c.Length < head.Length) continue;
            PartInfo p = new PartInfo
            {
                Kind = (PartKind)Enum.Parse(typeof(PartKind), c[col["type"]]),
                Name = c[col["nom"]], Variant = c[col["variante"]], Title = System.Text.RegularExpressions.Regex.Replace(c[col["titre"]], "^#[^ ]*", c[col["nom"]]), Source = c[col["source"]],
                Sizes = c[col["tailles"]], Unlocked = c[col["debloquee"]] == "oui", Cost = D(c[col["cout"]]),
                DryMassT = D(c[col["masse_seche_t"]]), WetMassT = D(c[col["masse_pleine_t"]]),
                ThrustVacKN = D(c[col["poussee_vide_kN"]]), ThrustAslKN = D(c[col["poussee_sol_kN"]]),
                IspVac = D(c[col["isp_vide_s"]]), IspAsl = D(c[col["isp_sol_s"]]), Propellants = c[col["ergols"]],
            };
            foreach (string r in c[col["ressources"]].Split(' '))
            {
                int eq = r.IndexOf('=');
                if (eq > 0) p.Resources.Add(new ResourceAmount { Name = r.Substring(0, eq), Amount = D(r.Substring(eq + 1)) });
            }
            parts.Add(p);
        }
        return parts;
    }

    static string Tanks(List<TankUse> tanks)
    {
        List<string> s = new List<string>();
        foreach (TankUse t in tanks) s.Add(t.Count + " × " + t.Tank.Title);
        return string.Join(", ", s.ToArray());
    }

    static int Main(string[] args)
    {
        Console.Error.WriteLine("chargement");
        List<PartInfo> parts = Load(args[0]);
        Console.Error.WriteLine(parts.Count + " pièces");
        bool all = args.Length > 3 && args[3] == "tout";
        if (all) foreach (PartInfo p in parts) p.Unlocked = true;
        if (args.Length > 3 && args[3] == "stock") parts = parts.FindAll(p => p.Unlocked && (p.Source == "KSP" || p.Source == "Making History"));
        DesignRequest req = new DesignRequest
        {
            Parts = parts,
            PayloadMass = args.Length > 1 ? D(args[1]) : 5,
            TargetDeltaV = args.Length > 2 ? D(args[2]) : 4500,
        };
        Stopwatch sw = Stopwatch.StartNew();
        LauncherDesigner.Log = m => Console.Error.WriteLine(sw.ElapsedMilliseconds + " ms " + m);
        LauncherDesigner designer = new LauncherDesigner(req);
        Console.Error.WriteLine(designer.Stats);
        List<LauncherDesign> designs = designer.Run();
        Console.WriteLine("Charge utile {0} t, Δv visé {1} m/s, calcul en {2} ms", req.PayloadMass, req.TargetDeltaV, sw.ElapsedMilliseconds);
        int bad = 0;
        foreach (LauncherDesign d in designs)
        {
            Console.WriteLine();
            Console.WriteLine("== " + d.Variant + (d.Valid ? "" : " — " + d.Problem));
            if (d.Stages.Count == 0) continue;
            Console.WriteLine("   masse {0:0.0} t, coût {1:0}, Δv {2:0} m/s, TWR décollage {3:0.00}", d.TotalMass, d.Cost, d.DeltaV, d.LiftoffTwr);
            for (int i = d.Stages.Count - 1; i >= 0; i--)
            {
                StagePlan s = d.Stages[i];
                Console.WriteLine("   Étage {0} : {1} × {2} ; {3} ; Δv {4:0}, TWR {5:0.00}", i + 1, s.EngineCount, s.Engine.Title, Tanks(s.Tanks), s.DeltaV, s.Twr);
                if (s.Boosters != null)
                    Console.WriteLine("      + {0} × {1}{2}", s.Boosters.Count, s.Boosters.Part.Title, s.Boosters.Solid ? "" : " avec " + Tanks(s.Boosters.Tanks));
            }
            foreach (string n in d.Notes) Console.WriteLine("   note : " + n);
            if (d.Valid && d.DeltaV < req.TargetDeltaV - 1) bad++;
        }
        return bad == 0 ? 0 : 1;
    }
}
