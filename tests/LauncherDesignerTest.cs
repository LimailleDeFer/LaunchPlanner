using System.Linq;
// Runs the launcher computation on an inventory exported from the game ("Export CSV" button).
//   mcs tests/LauncherDesignerTest.cs src/LauncherDesigner.cs src/PartInfo.cs src/CraftBuilder.cs src/CraftWriter.cs -out:build/designer.exe
//   mono build/designer.exe inventory.csv [payload mass t] [Δv m/s]
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
        // Older exports used French column names: map them to the English ones.
        string[][] legacy =
        {
            new[] { "name", "nom" }, new[] { "variant", "variante" }, new[] { "title", "titre" }, new[] { "sizes", "tailles" },
            new[] { "unlocked", "debloquee" }, new[] { "cost", "cout" }, new[] { "dry_mass_t", "masse_seche_t" },
            new[] { "wet_mass_t", "masse_pleine_t" }, new[] { "thrust_vac_kN", "poussee_vide_kN" }, new[] { "thrust_asl_kN", "poussee_sol_kN" },
            new[] { "isp_vac_s", "isp_vide_s" }, new[] { "isp_asl_s", "isp_sol_s" }, new[] { "propellants", "ergols" },
            new[] { "resources", "ressources" }, new[] { "category", "categorie" }, new[] { "diameter_m", "diametre_m" },
            new[] { "nose", "nez" }, new[] { "height_m", "hauteur_m" },
        };
        foreach (string[] pair in legacy)
            if (!col.ContainsKey(pair[0]) && col.ContainsKey(pair[1])) col[pair[0]] = col[pair[1]];
        for (int l = 1; l < lines.Length; l++)
        {
            string[] c = lines[l].Split(';');
            if (c.Length < head.Length) continue;
            PartInfo p = new PartInfo
            {
                Kind = (PartKind)Enum.Parse(typeof(PartKind), c[col["type"]]),
                Name = c[col["name"]], Variant = c[col["variant"]], Title = System.Text.RegularExpressions.Regex.Replace(c[col["title"]], "^#[^ ]*", c[col["name"]]), Source = c[col["source"]],
                Sizes = c[col["sizes"]], Unlocked = c[col["unlocked"]] == "yes" || c[col["unlocked"]] == "oui", Cost = D(c[col["cost"]]),
                DryMassT = D(c[col["dry_mass_t"]]), WetMassT = D(c[col["wet_mass_t"]]),
                ThrustVacKN = D(c[col["thrust_vac_kN"]]), ThrustAslKN = D(c[col["thrust_asl_kN"]]),
                IspVac = D(c[col["isp_vac_s"]]), IspAsl = D(c[col["isp_asl_s"]]), Propellants = c[col["propellants"]],
            };
            if (col.ContainsKey("category")) p.Category = c[col["category"]];
            if (col.ContainsKey("diameter_m") && c[col["diameter_m"]].Length > 0) p.DiameterM = double.Parse(c[col["diameter_m"]], CultureInfo.InvariantCulture);
            // Older exports have no storage column: the service tanks are the ones with storage.
            p.Storage = col.ContainsKey("storage") ? c[col["storage"]] == "yes" : p.Name.StartsWith("service-tank");
            if (col.ContainsKey("nose")) p.NoseTop = c[col["nose"]] == "yes" || c[col["nose"]] == "oui";
            if (col.ContainsKey("height_m") && c[col["height_m"]].Length > 0) p.HeightM = double.Parse(c[col["height_m"]], CultureInfo.InvariantCulture);
            foreach (string r in c[col["resources"]].Split(' '))
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
        Console.Error.WriteLine("loading");
        List<PartInfo> parts = Load(args[0]);
        Console.Error.WriteLine(parts.Count + " parts");
        bool all = args.Length > 3 && (args[3] == "all" || args[3] == "tout");
        if (all) foreach (PartInfo p in parts) p.Unlocked = true;
        if (args.Length > 3 && args[3] == "stock") parts = parts.FindAll(p => p.Unlocked && (p.Source == "KSP" || p.Source == "Making History"));
        DesignRequest req = new DesignRequest
        {
            Parts = parts,
            PayloadMass = args.Length > 1 ? D(args[1]) : 5,
            TargetDeltaV = args.Length > 2 ? D(args[2]) : 4500,
        };
        string twr = Environment.GetEnvironmentVariable("TWR");
        if (!string.IsNullOrEmpty(twr)) req.MinLiftoffTwr = D(twr);
        if (Environment.GetEnvironmentVariable("POWER") == "1") { req.Powerful = true; req.MinUpperTwr = LauncherDesigner.PowerfulUpperTwr; }
        // LIMITS=height,width,parts,mass (0 = no limit), like the VAB and launch pad in career
        string limits = Environment.GetEnvironmentVariable("LIMITS");
        if (!string.IsNullOrEmpty(limits))
        {
            string[] l = limits.Split(',');
            req.MaxHeight = D(l[0]); req.MaxWidth = D(l[1]); req.MaxParts = (int)D(l[2]); req.MaxMass = D(l[3]);
        }
        // LANDER=Δv,gravity: lander stage, with the stock legs
        string lander = Environment.GetEnvironmentVariable("LANDER");
        if (!string.IsNullOrEmpty(lander))
        {
            string[] l = lander.Split(',');
            req.LanderDeltaV = D(l[0]); req.LanderGravity = D(l[1]); req.LanderBody = "test";
            // Folded and deployed reach as the game's drag cubes give them (LT-1 read in PartDatabase.cfg, the others estimated)
            req.Legs.Add(new PartInfo { Name = "miniLandingLeg", Title = "LT-05", DryMassT = 0.015, Unlocked = true, Cost = 340, LegFoldedM = 0.6, LegDeployedM = 1.1 });
            req.Legs.Add(new PartInfo { Name = "landingLeg1", Title = "LT-1", DryMassT = 0.05, Unlocked = true, Cost = 440, LegFoldedM = 1.0, LegDeployedM = 1.98 });
            req.Legs.Add(new PartInfo { Name = "landingLeg1-2", Title = "LT-2", DryMassT = 0.1, Unlocked = true, Cost = 640, LegFoldedM = 1.4, LegDeployedM = 2.6 });
        }
        // ATMO=pressure (atm),entry speed (m/s): the destination has an atmosphere; stock-like chutes and shields
        string atmo = Environment.GetEnvironmentVariable("ATMO");
        if (!string.IsNullOrEmpty(atmo))
        {
            string[] l = atmo.Split(',');
            req.LanderPressureAtm = D(l[0]); req.LanderEntrySpeed = D(l[1]);
            req.Parachutes.Add(new PartInfo { Name = "radialDrogue", Title = "Mk12-R drogue", Sizes = "srf", DryMassT = 0.075, Cost = 150, ChuteDrag = 170, Drogue = true, Unlocked = true });
            req.Parachutes.Add(new PartInfo { Name = "parachuteRadial", Title = "Mk2-R", Sizes = "srf", DryMassT = 0.1, Cost = 400, ChuteDrag = 500, Unlocked = true });
            foreach (string size in new[] { "size1", "size2", "size3" })
                req.HeatShields.Add(new PartInfo { Name = "HeatShield" + size, Title = "Heat shield " + size, Sizes = size, Kind = PartKind.Decoupler,
                    DryMassT = 0.1 * CraftBuilder.DiameterOf(size), WetMassT = 0.3 * CraftBuilder.DiameterOf(size), DiameterM = CraftBuilder.DiameterOf(size) * 1.04, HeightM = 0.3, Cost = 600, Unlocked = true });
        }
        // Fake nose cones and multi-couplers (they are not in the CSV inventory), like the stock ones.
        // CONES=size1,size2: only these cone sizes are unlocked
        string cones = Environment.GetEnvironmentVariable("CONES");
        foreach (string size in string.IsNullOrEmpty(cones) ? new[] { "size0", "size1", "size1p5", "size2", "size3" } : cones.Split(','))
            req.NoseCones.Add(new PartInfo { Name = "fakeCone" + size, Title = "Cone " + size, Sizes = size, Kind = PartKind.Tank, Unlocked = true, DryMassT = 0.03 * CraftBuilder.DiameterOf(size), Cost = 300 });
        // Fake structural adapters 3.75 → 2.5 and 2.5 → 1.25 m (NOADAPTERS=1 to go without)
        if (Environment.GetEnvironmentVariable("NOADAPTERS") == null)
            foreach (string[] a in new[] { new[] { "size3", "size2" }, new[] { "size2", "size1" } })
                req.Adapters.Add(new PartInfo { Name = "fakeAdapter" + a[0] + a[1], Title = "Adapter " + a[0] + "→" + a[1], Sizes = a[1] + ", " + a[0],
                    Kind = PartKind.Tank, Unlocked = true, DryMassT = 0.1 * CraftBuilder.DiameterOf(a[0]), Cost = 400, AdapterTop = a[1], AdapterBottom = a[0], HeightM = 1 });
        if (Environment.GetEnvironmentVariable("NOCOUPLERS") == null)
            foreach (string size in new[] { "size1", "size2", "size3" })
                for (int n = 2; n <= 4; n++)
                {
                    double d = CraftBuilder.DiameterOf(size);
                    req.Couplers.Add(new PartInfo { Name = "fakeCoupler" + n + size, Title = "Coupler ×" + n + " " + size, Sizes = size, Kind = PartKind.Tank,
                        Unlocked = true, DryMassT = 0.1 * d, Cost = 500, StackOutputs = n, TopDiameterM = d, OutputDiameterM = d / 2,
                        OutputSpacingM = n == 2 ? d / 2 : d * 0.55, HeightM = 0.4 });
                }
        // Fake reaction wheels and RCS thrusters, like the stock ones (NOATTITUDE=1 to go without)
        if (Environment.GetEnvironmentVariable("NOATTITUDE") == null)
        {
            req.ReactionWheels.Add(new PartInfo { Name = "fakeWheelsize1", Title = "Wheel 1.25 m", Sizes = "size1", Kind = PartKind.Tank, Unlocked = true, DryMassT = 0.1, Cost = 1000, TorqueKNm = 15, HeightM = 0.4 });
            req.ReactionWheels.Add(new PartInfo { Name = "fakeWheelsize2", Title = "Wheel 2.5 m", Sizes = "size2", Kind = PartKind.Tank, Unlocked = true, DryMassT = 0.4, Cost = 2000, TorqueKNm = 30, HeightM = 0.6 });
            req.RcsThrusters.Add(new PartInfo { Name = "fakeRcs1", Title = "RCS block", Sizes = "srf", Kind = PartKind.Tank, Unlocked = true, DryMassT = 0.05, Cost = 620, RcsThrustKN = 1, RcsNozzles = 4, RcsIsp = 240 });
            req.RcsThrusters.Add(new PartInfo { Name = "fakeRcs2", Title = "Linear RCS", Sizes = "srf", Kind = PartKind.Tank, Unlocked = true, DryMassT = 0.03, Cost = 300, RcsThrustKN = 2, RcsNozzles = 1, RcsIsp = 240 });
        }
        // PAYLOAD=height,width,bottom size: shape of the payload (e.g. 3,2.5,size2)
        string shape = Environment.GetEnvironmentVariable("PAYLOAD");
        if (!string.IsNullOrEmpty(shape))
        {
            string[] l = shape.Split(',');
            req.PayloadHeight = D(l[0]); req.PayloadWidth = D(l[1]);
            if (l.Length > 2) req.PayloadBottomSize = l[2];
        }
        req.PayloadTorqueKNm = D(Environment.GetEnvironmentVariable("PAYLOADTORQUE") ?? "0");
        req.PayloadRcsKN = D(Environment.GetEnvironmentVariable("PAYLOADRCS") ?? "0");
        Stopwatch sw = Stopwatch.StartNew();
        LauncherDesigner.Log = m => Console.Error.WriteLine(sw.ElapsedMilliseconds + " ms " + m);
        LauncherDesigner designer = new LauncherDesigner(req);
        Console.Error.WriteLine(designer.Stats);
        List<LauncherDesign> designs = designer.Run();
        Console.WriteLine("Payload {0} t, target Δv {1} m/s, computed in {2} ms", req.PayloadMass, req.TargetDeltaV, sw.ElapsedMilliseconds);
        int bad = 0;
        foreach (LauncherDesign d in designs)
        {
            Console.WriteLine();
            Console.WriteLine("== " + d.Variant + (d.Valid ? "" : " — " + d.Problem));
            if (d.Stages.Count == 0) continue;
            Console.WriteLine("   mass {0:0.0} t, cost {1:0}, Δv {2:0} m/s (climb loss {4:0}), liftoff TWR {3:0.00}", d.TotalMass, d.Cost, d.DeltaV, d.LiftoffTwr, d.ClimbLoss);
            Console.WriteLine("   height {0:0.0} m, width {1:0.0} m, {2} parts", d.HeightM, d.WidthM, d.PartCount);
            if (d.Lander != null)
                Console.WriteLine("   Lander (Ø {0}): {1} × {2}; {3}; {4} × {5}; Δv {6:0}, TWR {7:0.00}, {8:0.0} t",
                    LauncherDesigner.Diameter(d.Lander.Size), d.Lander.EngineCount, d.Lander.Engine.Title, Tanks(d.Lander.Tanks),
                    d.LanderLegCount, d.LanderLegs != null ? d.LanderLegs.Title : "-", d.Lander.DeltaV, d.Lander.Twr, d.Lander.TotalMass);
            if (d.Lander != null)
                Console.WriteLine("     around it: {0} × {1}; height {2:0.0} m, width {3:0.0} m; {4}", d.Lander.RadialCount,
                    d.Lander.RadialTank != null ? d.Lander.RadialTank.Title : "-", d.Lander.HeightM, d.Lander.WidthM,
                    d.Lander.Attitude != null ? (d.Lander.Attitude.Rcs != null ? d.Lander.Attitude.Rings * d.Lander.Attitude.RcsPerRing + " RCS" : "no RCS")
                        + (d.Lander.Attitude.MonoTank != null ? ", " + d.Lander.Attitude.MonoTanks + " × " + d.Lander.Attitude.MonoTank.Title : "")
                        + (d.Lander.Attitude.Wheel != null ? ", " + d.Lander.Attitude.Wheels + " × " + d.Lander.Attitude.Wheel.Title : "") : "no attitude");
            for (int i = d.Stages.Count - 1; i >= 0; i--)
            {
                StagePlan s = d.Stages[i];
                Console.WriteLine("   Stage {0} (Ø {6}): {1} × {2}; {3}; Δv {4:0}, TWR {5:0.00}", i + 1, s.EngineCount, s.Engine.Title, Tanks(s.Tanks), s.DeltaV, s.Twr, LauncherDesigner.Diameter(s.Size));
                if (s.Coupler != null) Console.WriteLine("      under a " + s.Coupler.Title);
                AttitudePlan a = s.Attitude;
                if (a != null)
                    Console.WriteLine("      attitude: {0} × {1} at joint {2} ({3:0} / {4:0} kN·m); {5} × {6}; {7} × {8} ({9:0.00} t mono{10})",
                        a.Wheels, a.Wheel != null ? a.Wheel.Title : "-", a.Joint, a.TorqueKNm, a.NeededTorqueKNm, a.Rings * a.RcsPerRing, a.Rcs != null ? a.Rcs.Title : "-",
                        a.MonoTanks, a.MonoTank != null ? a.MonoTank.Title : "-", a.MonoT, a.MonoRadial ? ", radial" : "");
                if (s.Boosters != null)
                    Console.WriteLine("      + {0} × {4}{1}{2}{3}", s.Boosters.Count, s.Boosters.Part.Title, s.Boosters.Solid ? "" : " with " + Tanks(s.Boosters.Tanks),
                        s.Boosters.NoseCone != null ? ", " + string.Join(" + ", s.Boosters.NoseAdapters.ConvertAll(x => x.Title).ToArray()) + (s.Boosters.NoseAdapters.Count > 0 ? " + " : "") + s.Boosters.NoseCone.Title : "",
                        s.Boosters.EnginesEach > 1 ? "(" + s.Boosters.EnginesEach + " engines" + (s.Boosters.Coupler != null ? " under " + s.Boosters.Coupler.Title : "") + ") " : "");
                    Console.WriteLine("      core alone after the drop: TWR {0:0.00}", s.CoreTwr);
                if (s.Boosters != null && !s.Boosters.Solid && !s.Boosters.Crossfeed)
                {
                    double tc = (s.Fuel + s.EngineFuel) / (s.EngineCount * s.Engine.ThrustVacKN / (s.Engine.IspVac * 9.80665));
                    double tb = s.Boosters.Fuel / (s.Boosters.EnginesEach * s.Boosters.Part.ThrustVacKN / (s.Boosters.Part.IspVac * 9.80665));
                    Console.WriteLine("      burn: core {0:0} s, boosters {1:0} s{2}", tc, tb, tb > tc ? "  ERROR: the core flames out first" : "");
                }
            }
            foreach (string n in d.Notes) Console.WriteLine("   note: " + n);
            if (d.Valid && d.DeltaV < req.TargetDeltaV - 1) bad++;
        }
        // Vessel generation (step 4) with approximate part shapes: CRAFT=folder writes one .craft per variant.
        string craftDir = Environment.GetEnvironmentVariable("CRAFT");
        if (!string.IsNullOrEmpty(craftDir))
            foreach (LauncherDesign d in designs)
            {
                if (!d.Valid) continue;
                CraftBuilder builder = new CraftBuilder(new FakeShapes()) { FuelLine = new PartInfo { Name = "fuelLine", Title = "FTX-2", Kind = PartKind.Tank } };
                List<CraftPart> craft = builder.Build(d);
                bad += CheckCraft(craft);
                if (d.Lander != null) bad += CheckLander(craft, d);
                string path = System.IO.Path.Combine(craftDir, d.Variant.Replace(' ', '-') + ".craft");
                System.IO.File.WriteAllText(path, CraftWriter.Write(builder, craft, d.Variant, "1.12.5").ToText());
                Console.WriteLine("{0}: {1} parts (estimated {4}), {2} activation stages → {3}", d.Variant, craft.Count, builder.ActionCount, path, d.PartCount);
                foreach (string n in builder.Notes) Console.WriteLine("   note: " + n);
            }
        return bad == 0 ? 0 : 1;
    }

    /// <summary>Checks link consistency: a single root, reciprocal parents and children, reciprocal symmetries.</summary>
    /// <summary>Lander geometry: radial tanks against the central tank without overlapping, folded legs above the engine's bottom.</summary>
    static int CheckLander(List<CraftPart> craft, LauncherDesign d)
    {
        int errors = 0;
        // Down the stack from the root: wheels, central tanks, then the lander's engines.
        CraftPart core = null, p0 = craft[0];
        while (p0 != null && p0.Info.Kind == PartKind.Tank)
        {
            if (p0.Info.TorqueKNm <= 0) core = core ?? p0;
            CraftPart next = p0.Children.Find(c => c.Attach == AttachKind.Stack);
            if (next == null || next.Info.Kind != PartKind.Tank) break;
            p0 = next;
        }
        CraftPart last = p0;
        List<CraftPart> radials = craft.FindAll(p => p.Info == d.Lander.RadialTank && p.Attach == AttachKind.Surface);
        List<CraftPart> legs = craft.FindAll(p => p.Info == d.LanderLegs);
        double bottom = double.MaxValue;
        foreach (CraftPart p in last.Children)
            if (p.Info == d.Lander.Engine) bottom = Math.Min(bottom, p.Pos.Y + p.Shape.Lowest);
        foreach (CraftPart c in last.Children)
            if (c.Info.StackOutputs > 0) foreach (CraftPart p in c.Children) bottom = Math.Min(bottom, p.Pos.Y + p.Shape.Lowest);
        double coreR = core.Shape.Diameter / 2;
        foreach (CraftPart r in radials)
        {
            double dist = Math.Sqrt(r.Pos.X * r.Pos.X + r.Pos.Z * r.Pos.Z), rr = r.Shape.Diameter / 2;
            if (Math.Abs(dist - coreR - rr) > 0.01) { Console.WriteLine("   ERROR: radial tank at {0:0.00} m from the axis, {1:0.00} expected", dist, coreR + rr); errors++; }
            foreach (CraftPart q in radials)
                if (q != r && (q.Pos - r.Pos).Length < 2 * rr - 0.01) { Console.WriteLine("   ERROR: radial tanks overlap"); errors++; }
            double top = r.Pos.Y + r.Shape.TopY, coreTop = core.Pos.Y + core.Shape.TopY;
            if (Math.Abs(top - coreTop) > 0.01) { Console.WriteLine("   ERROR: radial top {0:0.00}, core top {1:0.00}", top, coreTop); errors++; }
            if (r.Pos.Y + r.Shape.Lowest < bottom - 0.01) { Console.WriteLine("   ERROR: radial tank below the engine"); errors++; }
        }
        foreach (CraftPart l in legs)
        {
            double foot = l.Pos.Y + l.Shape.MinY;
            double reach = Math.Sqrt(l.Pos.X * l.Pos.X + l.Pos.Z * l.Pos.Z);
            if (foot < bottom - 0.01) { Console.WriteLine("   ERROR: folded leg foot at {0:0.00}, engine bottom {1:0.00}", foot, bottom); errors++; }
            if (reach < coreR - 0.01) { Console.WriteLine("   ERROR: leg inside the central tank"); errors++; }
        }
        // Parts around the central tank: no two at the same heading and height (legs, thrusters, tanks, parachutes).
        List<CraftPart> around = craft.FindAll(p => p.Attach == AttachKind.Surface && p.Parent != null && p.Parent.Info.Kind == PartKind.Tank
            && p.Parent.Attach != AttachKind.Surface && p.Info != d.Lander.RadialTank && p.Parent.Pos.Y > bottom);
        foreach (CraftPart p in around)
            foreach (CraftPart q in around)
            {
                if (q.Id <= p.Id || p.Parent.Pos.X != q.Parent.Pos.X || p.Parent.Pos.Z != q.Parent.Pos.Z) continue;
                Vec3 axis = p.Parent.Pos;
                double hp = new Vec3(p.Pos.X - axis.X, 0, p.Pos.Z - axis.Z).Heading, hq = new Vec3(q.Pos.X - axis.X, 0, q.Pos.Z - axis.Z).Heading;
                double gap = Math.Abs(((hp - hq) % 360 + 540) % 360 - 180);
                double pl = p.Pos.Y + p.Shape.Lowest, ph = p.Pos.Y + p.Shape.Diameter / 2, ql = q.Pos.Y + q.Shape.Lowest, qh = q.Pos.Y + q.Shape.Diameter / 2;
                double r = p.Parent.Shape.Diameter / 2, need = (Math.Atan2(p.Shape.Diameter / 2, r) + Math.Atan2(q.Shape.Diameter / 2, r)) * 180 / Math.PI;
                if (gap < need - 1 && pl < qh && ql < ph)
                { Console.WriteLine("   ERROR: {0} and {1} at the same place (heading {2:0}/{3:0})", p.Info.Title, q.Info.Title, hp, hq); errors++; }
            }
        foreach (CraftPart p in craft)
            if (p.Info.ChuteDrag > 0 || p.Info.Name.StartsWith("HeatShield") || p.Info == d.Lander.Engine && p.Activation >= 0 && last.Children.Contains(p))
                Console.WriteLine("   lander part {0} at y {1:0.00}, heading {2:0}, activation {3}", p.Info.Title, p.Pos.Y, new Vec3(p.Pos.X, 0, p.Pos.Z).Heading, p.Activation);
        double tanksBottom = core.Pos.Y + core.Shape.BottomY;
        foreach (CraftPart l in legs)
            if (l.Pos.Y + l.Shape.MinY < tanksBottom - 0.01) { Console.WriteLine("   note: folded foot {0:0.00} below the tanks' bottom {1:0.00}", l.Pos.Y + l.Shape.MinY, tanksBottom); break; }
        Console.WriteLine("   lander geometry: core Ø {0:0.##}, {1} radial tanks, {2} legs, engine bottom {3:0.00}, lowest folded foot {4:0.00}",
            core.Shape.Diameter, radials.Count, legs.Count, bottom, legs.Count > 0 ? legs.ConvertAll(l => l.Pos.Y + l.Shape.MinY).Min() : 0);
        return errors;
    }

    static int CheckCraft(List<CraftPart> craft)
    {
        int errors = 0;
        int roots = craft.FindAll(p => p.Parent == null).Count;
        if (roots != 1) { Console.WriteLine("   ERROR: {0} roots", roots); errors++; }
        if (craft[0].Parent != null) { Console.WriteLine("   ERROR: the first part is not the root"); errors++; }
        foreach (CraftPart p in craft)
        {
            if (p.Parent != null && !p.Parent.Children.Contains(p)) { Console.WriteLine("   ERROR: missing link " + p.Key); errors++; }
            foreach (CraftPart q in p.Symmetry)
                if (!q.Symmetry.Contains(p) || q.Info != p.Info) { Console.WriteLine("   ERROR: symmetry " + p.Key); errors++; }
            if (p.Attach == AttachKind.Stack && (p.SelfNode == null || p.ParentNode == null)) { Console.WriteLine("   ERROR: node " + p.Key); errors++; }
            if (p.Info.Name.Contains("_")) { Console.WriteLine("   ERROR: name with _ " + p.Key); errors++; }
        }
        return errors;
    }
}

/// <summary>Approximate shapes: tank height from volume, 1.5 m engines, 0.2 m decouplers.</summary>
class FakeShapes : IPartShapes
{
    public PartShape Get(PartInfo p)
    {
        double d = CraftBuilder.DiameterOf(LauncherDesigner.SizeOf(p));
        double h = p.Kind == PartKind.Decoupler ? 0.2 : p.Kind == PartKind.Engine ? 1.5
            : Math.Max(0.3, (p.WetMassT - p.DryMassT) / 1.0 / (Math.PI * d * d / 4));
        if (p.Name.StartsWith("fakeCone")) h = d;
        if (p.StackOutputs > 0 || p.TorqueKNm > 0) h = p.HeightM;
        if (p.RcsThrustKN > 0) { d = 0.3; h = 0.3; }
        if (p.ChuteDrag > 0 || (p.Sizes ?? "").Trim() == "srf") { d = 0.4; h = 0.5; }
        PartShape s = new PartShape { Diameter = d, TopNode = "top", BottomNode = "bottom", TopY = h / 2, BottomY = -h / 2 };
        if (p.Name.StartsWith("fakeCone")) { s.TopNode = "bottom"; s.BottomNode = null; s.TopY = -h / 2; }
        for (int i = 0; i < p.StackOutputs; i++)
        {
            s.OutputNodes.Add("bottom" + (i + 1));
            s.OutputPos.Add(new Vec3(0, -h / 2, p.OutputSpacingM / (2 * Math.Sin(Math.PI / p.StackOutputs))).Yaw(360.0 * i / p.StackOutputs));
        }
        s.SrfPos = new Vec3(0, 0, p.Kind == PartKind.Decoupler ? 0.1 : -d / 2);
        s.SrfDir = new Vec3(0, 0, p.Kind == PartKind.Decoupler ? -1 : 1);
        // Landing legs, like the stock LT-1: attached high on their bracket, the folded foot about a metre below.
        if (p.Name.ToLower().Contains("landingleg"))
        {
            s = new PartShape { Diameter = 0.4, SrfPos = new Vec3(0, 0.2, 0), SrfDir = new Vec3(0, 0, 1), MinY = 0.2 - p.LegFoldedM };
        }
        // Engines: the nozzle ends a little below the bottom node.
        if (p.Kind == PartKind.Engine) s.MinY = -h / 2 - 0.1;
        s.Modules.Add("ModulePartVariants"); s.ModuleIds.Add("");
        if (p.Variant.Length > 0) { s.Modules.Add("ModuleB9PartSwitch"); s.ModuleIds.Add(p.VariantModule); }
        if (p.Kind == PartKind.Decoupler) { s.Modules.Add("ModuleToggleCrossfeed"); s.ModuleIds.Add(""); }
        return s;
    }
}
