// Lance le calcul des lanceurs sur un inventaire exporté du jeu (bouton « Exporter CSV »).
//   mcs tests/LauncherDesignerTest.cs src/LauncherDesigner.cs src/PartInfo.cs src/CraftBuilder.cs src/CraftWriter.cs -out:build/designer.exe
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
            if (col.ContainsKey("categorie")) p.Category = c[col["categorie"]];
            if (col.ContainsKey("hauteur_m") && c[col["hauteur_m"]].Length > 0) p.HeightM = double.Parse(c[col["hauteur_m"]], CultureInfo.InvariantCulture);
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
        // LIMITS=hauteur,largeur,pièces,masse (0 = sans limite), comme le VAB et le pas de tir en carrière
        string limits = Environment.GetEnvironmentVariable("LIMITS");
        if (!string.IsNullOrEmpty(limits))
        {
            string[] l = limits.Split(',');
            req.MaxHeight = D(l[0]); req.MaxWidth = D(l[1]); req.MaxParts = (int)D(l[2]); req.MaxMass = D(l[3]);
        }
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
            Console.WriteLine("   hauteur {0:0.0} m, largeur {1:0.0} m, {2} pièces", d.HeightM, d.WidthM, d.PartCount);
            for (int i = d.Stages.Count - 1; i >= 0; i--)
            {
                StagePlan s = d.Stages[i];
                Console.WriteLine("   Étage {0} (Ø {6}) : {1} × {2} ; {3} ; Δv {4:0}, TWR {5:0.00}", i + 1, s.EngineCount, s.Engine.Title, Tanks(s.Tanks), s.DeltaV, s.Twr, LauncherDesigner.Diameter(s.Size));
                if (s.Boosters != null)
                    Console.WriteLine("      + {0} × {1}{2}", s.Boosters.Count, s.Boosters.Part.Title, s.Boosters.Solid ? "" : " avec " + Tanks(s.Boosters.Tanks));
            }
            foreach (string n in d.Notes) Console.WriteLine("   note : " + n);
            if (d.Valid && d.DeltaV < req.TargetDeltaV - 1) bad++;
        }
        // Génération du vaisseau (étape 4) avec des formes de pièces approximatives : CRAFT=dossier écrit un .craft par variante.
        string craftDir = Environment.GetEnvironmentVariable("CRAFT");
        if (!string.IsNullOrEmpty(craftDir))
            foreach (LauncherDesign d in designs)
            {
                if (!d.Valid) continue;
                CraftBuilder builder = new CraftBuilder(new FakeShapes());
                List<CraftPart> craft = builder.Build(d);
                bad += CheckCraft(craft);
                string path = System.IO.Path.Combine(craftDir, d.Variant.Replace(' ', '-') + ".craft");
                System.IO.File.WriteAllText(path, CraftWriter.Write(builder, craft, d.Variant, "1.12.5").ToText());
                Console.WriteLine("{0} : {1} pièces, {2} étages d'activation → {3}", d.Variant, craft.Count, builder.ActionCount, path);
                foreach (string n in builder.Notes) Console.WriteLine("   note : " + n);
            }
        return bad == 0 ? 0 : 1;
    }

    /// <summary>Vérifie la cohérence des liens : une seule racine, parents et enfants réciproques, symétries réciproques.</summary>
    static int CheckCraft(List<CraftPart> craft)
    {
        int errors = 0;
        int roots = craft.FindAll(p => p.Parent == null).Count;
        if (roots != 1) { Console.WriteLine("   ERREUR : {0} racines", roots); errors++; }
        if (craft[0].Parent != null) { Console.WriteLine("   ERREUR : la première pièce n'est pas la racine"); errors++; }
        foreach (CraftPart p in craft)
        {
            if (p.Parent != null && !p.Parent.Children.Contains(p)) { Console.WriteLine("   ERREUR : lien manquant " + p.Key); errors++; }
            foreach (CraftPart q in p.Symmetry)
                if (!q.Symmetry.Contains(p) || q.Info != p.Info) { Console.WriteLine("   ERREUR : symétrie " + p.Key); errors++; }
            if (p.Attach == AttachKind.Stack && (p.SelfNode == null || p.ParentNode == null)) { Console.WriteLine("   ERREUR : nœud " + p.Key); errors++; }
            if (p.Info.Name.Contains("_")) { Console.WriteLine("   ERREUR : nom avec _ " + p.Key); errors++; }
        }
        return errors;
    }
}

/// <summary>Formes approximatives : hauteur des réservoirs selon leur volume, moteurs de 1,5 m, découpleurs de 0,2 m.</summary>
class FakeShapes : IPartShapes
{
    public PartShape Get(PartInfo p)
    {
        double d = CraftBuilder.DiameterOf(LauncherDesigner.SizeOf(p));
        double h = p.Kind == PartKind.Decoupler ? 0.2 : p.Kind == PartKind.Engine ? 1.5
            : Math.Max(0.3, (p.WetMassT - p.DryMassT) / 1.0 / (Math.PI * d * d / 4));
        PartShape s = new PartShape { Diameter = d, TopNode = "top", BottomNode = "bottom", TopY = h / 2, BottomY = -h / 2 };
        s.SrfPos = new Vec3(0, 0, p.Kind == PartKind.Decoupler ? 0.1 : -d / 2);
        s.SrfDir = new Vec3(0, 0, p.Kind == PartKind.Decoupler ? -1 : 1);
        s.Modules.Add("ModulePartVariants"); s.ModuleIds.Add("");
        if (p.Variant.Length > 0) { s.Modules.Add("ModuleB9PartSwitch"); s.ModuleIds.Add(p.VariantModule); }
        if (p.Kind == PartKind.Decoupler) { s.Modules.Add("ModuleToggleCrossfeed"); s.ModuleIds.Add(""); }
        return s;
    }
}
