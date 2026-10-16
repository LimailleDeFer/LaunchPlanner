using System;
using System.Collections.Generic;

namespace LaunchPlanner
{
    /// <summary>What the builder needs to know about a part's shape (read from the part's model in the game).</summary>
    public class PartShape
    {
        public string TopNode;          // top stack node (null if none)
        public string BottomNode;       // bottom stack node
        public double TopY, BottomY;    // height of these nodes in the part's frame
        public Vec3 SrfPos;             // surface attach point
        public Vec3 SrfDir = new Vec3(0, 0, 1);   // direction of the surface node
        public double Diameter = 1.25;
        public List<string> Modules = new List<string>();      // module names, in model order
        public List<string> ModuleIds = new List<string>();    // moduleID (B9PartSwitch), "" otherwise
        public Vec3 ExhaustDir = new Vec3(0, -1, 0);           // exhaust direction of an engine, in the part's frame
        public List<string> OutputNodes = new List<string>();  // multi-coupler: bottom nodes
        public List<Vec3> OutputPos = new List<Vec3>();

        public double Height { get { return TopNode != null && BottomNode != null ? TopY - BottomY : Diameter; } }
    }

    public interface IPartShapes
    {
        PartShape Get(PartInfo part);
    }

    public struct Vec3
    {
        public double X, Y, Z;

        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vec3 operator *(Vec3 a, double k) { return new Vec3(a.X * k, a.Y * k, a.Z * k); }

        /// <summary>Rotation around the vertical axis, in Unity's direction (from +Z toward +X).</summary>
        public Vec3 Yaw(double degrees)
        {
            double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            return new Vec3(X * c + Z * s, Y, -X * s + Z * c);
        }

        public double Heading { get { return Math.Atan2(X, Z) * 180 / Math.PI; } }

        public double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public Vec3 Unit { get { double l = Length; return l > 1e-9 ? this * (1 / l) : new Vec3(0, 1, 0); } }
        public static double Dot(Vec3 a, Vec3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        public static Vec3 Cross(Vec3 a, Vec3 b) { return new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
    }

    /// <summary>Quaternion in Unity's convention (x, y, z, w), for parts that are not only rotated around the vertical.</summary>
    public struct Quat
    {
        public double X, Y, Z, W;

        public Quat(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }

        public static Quat FromYaw(double degrees)
        {
            double a = degrees * Math.PI / 360;
            return new Quat(0, Math.Sin(a), 0, Math.Cos(a));
        }

        /// <summary>Smallest rotation that takes direction `from` onto `to`.</summary>
        public static Quat FromTo(Vec3 from, Vec3 to)
        {
            Vec3 a = from.Unit, b = to.Unit;
            double d = Vec3.Dot(a, b);
            if (d < -0.999999)
            {
                Vec3 axis = Vec3.Cross(new Vec3(1, 0, 0), a);
                if (axis.Length < 1e-6) axis = Vec3.Cross(new Vec3(0, 1, 0), a);
                axis = axis.Unit;
                return new Quat(axis.X, axis.Y, axis.Z, 0);
            }
            Vec3 c = Vec3.Cross(a, b);
            return new Quat(c.X, c.Y, c.Z, 1 + d).Normalized;
        }

        public Quat Normalized
        {
            get
            {
                double l = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
                return new Quat(X / l, Y / l, Z / l, W / l);
            }
        }

        public Quat Inverse { get { return new Quat(-X, -Y, -Z, W); } }

        public static Quat operator *(Quat a, Quat b)
        {
            return new Quat(
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
        }

        public Vec3 Rotate(Vec3 v)
        {
            Vec3 u = new Vec3(X, Y, Z);
            Vec3 t = Vec3.Cross(u, v) * 2;
            return v + t * W + Vec3.Cross(u, t);
        }
    }

    public enum AttachKind { Root, Stack, Surface }

    public class CraftPart
    {
        public uint Id;
        public PartInfo Info;
        public PartShape Shape;
        public Vec3 Pos;
        public double Yaw;                  // rotation around the vertical, in degrees
        public Quat? Tilted;                // full rotation, when the part is not upright (separation motors)
        public CraftPart Parent;
        public AttachKind Attach = AttachKind.Root;
        public string SelfNode;             // this part's node facing the parent
        public string ParentNode;           // parent's node (stack attachment)
        public readonly List<CraftPart> Children = new List<CraftPart>();
        public readonly List<CraftPart> Symmetry = new List<CraftPart>();
        public int Activation = -1;         // activation rank (0 = first press of space), -1 if the part has no stage icon
        public int Separation = -1;         // activation rank of the decoupler that drops this part
        public bool Crossfeed;              // radial decoupler of asparagus staging: lets fuel through
        public CraftPart FuelTarget;        // fuel line: part being fed
        public Vec3 TargetPoint;            // fuel line: end point on the fed part

        public string Key { get { return Info.Name + "_" + Id; } }

        public Quat Rotation { get { return Tilted ?? Quat.FromYaw(Yaw); } }
    }

    /// <summary>
    /// Turns a launcher variant into a list of placed and connected parts, ready to become a vessel.
    /// The root is the decoupler under the payload (or the first tank of the last stage); its top node
    /// stays free to attach the payload.
    /// </summary>
    public class CraftBuilder
    {
        readonly IPartShapes shapes;
        readonly List<CraftPart> parts = new List<CraftPart>();
        public readonly List<string> Notes = new List<string>();
        uint nextId = 100001;

        /// <summary>First part id: above those of the payload, so that no two parts of the vessel share a key.</summary>
        public uint FirstId { set { nextId = Math.Max(nextId, value); } }
        int actions;

        /// <summary>Unlocked fuel line (FTX-2) for asparagus staging; null to go through the decouplers.</summary>
        public PartInfo FuelLine;

        public CraftBuilder(IPartShapes shapes)
        {
            this.shapes = shapes;
        }

        public static double DiameterOf(string size)
        {
            // size0 = 0.625 m, size1 = 1.25 m, size1p5 = 1.875 m, size2 = 2.5 m...
            if (string.IsNullOrEmpty(size) || !size.StartsWith("size")) return 1.25;
            double v;
            if (!double.TryParse(size.Substring(4).Replace('p', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v)) return 1.25;
            return v >= 1 ? 1.25 * v : 0.625 + 0.625 * v;
        }

        CraftPart Make(PartInfo info)
        {
            CraftPart p = new CraftPart { Id = nextId++, Info = info, Shape = shapes.Get(info) };
            parts.Add(p);
            return p;
        }

        static void Link(CraftPart child, CraftPart parent, AttachKind kind)
        {
            child.Parent = parent;
            child.Attach = kind;
            parent.Children.Add(child);
        }

        /// <summary>Attaches `child` below `parent` (parent's bottom node against the child's top node).</summary>
        CraftPart StackBelow(CraftPart parent, PartInfo info)
        {
            CraftPart c = Make(info);
            double parentBottom = parent.Shape.BottomNode != null ? parent.Shape.BottomY : -parent.Shape.Height / 2;
            double childTop = c.Shape.TopNode != null ? c.Shape.TopY : c.Shape.Height / 2;
            c.Pos = new Vec3(parent.Pos.X, parent.Pos.Y + parentBottom - childTop, parent.Pos.Z);
            c.Yaw = parent.Yaw;
            Link(c, parent, AttachKind.Stack);
            c.SelfNode = c.Shape.TopNode ?? "top";
            c.ParentNode = parent.Shape.BottomNode ?? "bottom";
            return c;
        }

        /// <summary>
        /// Attaches `child` to a surface at `point` on a surface with normal `outward`, using the same rule as the editor:
        /// rotation = LookRotation(normal) × LookRotation(direction of the surface node).
        /// </summary>
        CraftPart SurfaceAt(CraftPart parent, PartInfo info, Vec3 point, Vec3 outward)
        {
            CraftPart c = Make(info);
            c.Yaw = outward.Heading + c.Shape.SrfDir.Heading;
            c.Pos = point - c.Shape.SrfPos.Yaw(c.Yaw);
            Link(c, parent, AttachKind.Surface);
            c.SelfNode = "srfAttach";
            return c;
        }

        /// <summary>Places `child` on top of `parent` (parent's top node against the child's bottom node, or its only node).</summary>
        CraftPart StackAbove(CraftPart parent, PartInfo info)
        {
            CraftPart c = Make(info);
            double parentTop = parent.Shape.TopNode != null ? parent.Shape.TopY : parent.Shape.Height / 2;
            string node = c.Shape.BottomNode ?? c.Shape.TopNode;
            double childBottom = c.Shape.BottomNode != null ? c.Shape.BottomY : c.Shape.TopNode != null ? c.Shape.TopY : -c.Shape.Height / 2;
            c.Pos = new Vec3(parent.Pos.X, parent.Pos.Y + parentTop - childBottom, parent.Pos.Z);
            c.Yaw = parent.Yaw;
            Link(c, parent, AttachKind.Stack);
            c.SelfNode = node ?? "bottom";
            c.ParentNode = parent.Shape.TopNode ?? "top";
            return c;
        }

        static double Bottom(CraftPart p) { return p.Pos.Y + (p.Shape.BottomNode != null ? p.Shape.BottomY : -p.Shape.Height / 2); }
        static double Top(CraftPart p) { return p.Pos.Y + (p.Shape.TopNode != null ? p.Shape.TopY : p.Shape.Height / 2); }

        public List<CraftPart> Build(LauncherDesign d)
        {
            parts.Clear();
            Notes.Clear();
            int k = d.Stages.Count;

            // Activation ranks: first-stage engines and boosters, booster drop, then each upper stage
            // (decoupler and ignition together), finally the payload drop.
            BoosterPlan boosters = d.Stages[0].Boosters;
            int boosterDrops = boosters == null || boosters.Count == 0 ? 0 : boosters.Crossfeed ? boosters.Count / 2 : 1;
            bool lander = d.Lander != null;
            actions = 1 + boosterDrops + (k - 1) + (d.PayloadDecoupler != null ? 1 : 0) + (lander ? 1 : 0);

            CraftPart cursor = null;
            if (lander) cursor = AddLander(d);
            if (d.PayloadDecoupler != null)
            {
                cursor = cursor == null ? Make(d.PayloadDecoupler) : StackBelowEngines(cursor, d.PayloadDecoupler);
                cursor.Activation = actions - 1 - (lander ? 1 : 0);
            }
            else
            {
                Notes.Add("No decoupler under the payload: it will stay attached to the last stage.");
                if (lander) cursor = landerEngine ?? cursor;
            }

            for (int i = k - 1; i >= 0; i--)
            {
                StagePlan s = d.Stages[i];
                int ignition = i == 0 ? 0 : boosterDrops + i;
                List<CraftPart> stageParts = new List<CraftPart>();
                CraftPart firstTank = null;
                foreach (TankUse t in s.Tanks)
                    for (int n = 0; n < t.Count; n++)
                    {
                        cursor = cursor == null ? Make(t.Tank) : StackBelow(cursor, t.Tank);
                        if (firstTank == null) firstTank = cursor;
                        stageParts.Add(cursor);
                    }
                int count = s.EngineCount;
                if (cursor == null) { cursor = Make(s.Engine); count--; }   // stage without a tank: the engine serves as the root
                List<CraftPart> engines = AddEngines(cursor, s, count, ignition);
                stageParts.AddRange(engines);

                if (i == 0)
                {
                    if (boosters != null && boosters.Count > 0) AddBoosters(stageParts, engines, boosters, s);
                    break;
                }
                // The decoupler under the stage: under the central engine, otherwise under the last tank (the engines surround it).
                CraftPart below = engines.Count == 1 && engines[0].Attach == AttachKind.Stack ? engines[0] : cursor;
                if (s.Decoupler != null)
                {
                    CraftPart dec = StackBelow(below, s.Decoupler);
                    if (below != engines[0] && engines.Count > 0)
                    {
                        dec.Pos.Y -= engines[0].Shape.Height;
                        Notes.Add(string.Format("Stage {0}: its engines are around the tank, so the decoupler sits below their nozzles, " +
                            "with a gap: add an interstage or a fairing around them if you have one.", i + 1));
                    }
                    dec.Activation = ignition;
                    cursor = dec;
                }
                else
                {
                    Notes.Add("No unlocked decoupler to separate stage " + (i + 1) + ".");
                    cursor = below;
                }
            }

            AssignSeparation();
            return parts;
        }

        CraftPart landerTank, landerEngine;

        /// <summary>
        /// Lander stage, root of the launcher: the payload attaches to its top tank. Legs in a cross on the
        /// bottom tank; the engine ignites after the launcher's last stage is dropped.
        /// </summary>
        CraftPart AddLander(LauncherDesign d)
        {
            StagePlan s = d.Lander;
            CraftPart cursor = null;
            foreach (TankUse t in s.Tanks)
                for (int n = 0; n < t.Count; n++) cursor = cursor == null ? Make(t.Tank) : StackBelow(cursor, t.Tank);
            List<CraftPart> engines = AddEngines(cursor, s, s.EngineCount, actions - 1);
            landerTank = cursor;
            landerEngine = engines.Count == 1 && engines[0].Attach == AttachKind.Stack ? engines[0] : null;

            if (d.LanderLegs != null && d.LanderLegCount > 0)
            {
                List<CraftPart> legs = new List<CraftPart>();
                double span = Top(cursor) - Bottom(cursor);
                double y = Bottom(cursor) + Math.Min(0.4, 0.25 * span);
                for (int i = 0; i < d.LanderLegCount; i++)
                {
                    Vec3 outward = new Vec3(0, 0, 1).Yaw(45 + 360.0 * i / d.LanderLegCount);
                    Vec3 point = new Vec3(cursor.Pos.X, y, cursor.Pos.Z) + outward * (cursor.Shape.Diameter / 2);
                    legs.Add(SurfaceAt(cursor, d.LanderLegs, point, outward));
                }
                SetSymmetry(legs);
                Notes.Add(string.Format("Lander: {0} × {1} (press G to deploy them). Check in the editor that they extend lower than the nozzle.",
                    d.LanderLegCount, d.LanderLegs.Title));
            }
            else Notes.Add("No landing legs unlocked: the lander will land on its nozzle.");
            return cursor;
        }

        /// <summary>Decoupler under the lander: under its central engine, otherwise under its tank (engines in a ring).</summary>
        CraftPart StackBelowEngines(CraftPart tank, PartInfo decoupler)
        {
            CraftPart below = landerEngine ?? tank;
            CraftPart dec = StackBelow(below, decoupler);
            if (landerEngine == null)
            {
                double lowest = Bottom(tank);
                foreach (CraftPart c in tank.Children) if (c.Info.Kind == PartKind.Engine) lowest = Math.Min(lowest, Bottom(c));
                dec.Pos.Y -= Bottom(tank) - lowest;
            }
            return dec;
        }

        /// <summary>Engines under the last tank: one at the center on the node, the others in a ring attached to the surface.</summary>
        List<CraftPart> AddEngines(CraftPart tank, StagePlan s, int n, int ignition)
        {
            if (tank.Info == s.Engine) { tank.Activation = ignition; return new List<CraftPart> { tank }; }
            // With boosters, the core's ring is turned so its engines sit between the boosters, not in front of them.
            double baseAngle = s.Boosters != null && s.Boosters.Count > 0 ? 180.0 / s.Boosters.Count : 0;
            return AddEngines(tank, s.Engine, n, s.Coupler, ignition, true, baseAngle);
        }

        /// <summary>
        /// Engines under a tank. Without <paramref name="symmetry"/> the ring is not linked in symmetry (each booster's
        /// engines are paired with the same engine of the other boosters instead).
        /// </summary>
        List<CraftPart> AddEngines(CraftPart tank, PartInfo engine, int n, PartInfo couplerInfo, int ignition, bool symmetry, double baseAngle = 0)
        {
            List<CraftPart> engines = new List<CraftPart>();
            if (n <= 0) return engines;
            if (couplerInfo != null)
            {
                CraftPart coupler = StackBelow(tank, couplerInfo);
                if (coupler.Shape.OutputNodes.Count == n)
                {
                    for (int j = 0; j < n; j++)
                    {
                        CraftPart e = Make(engine);
                        Vec3 node = coupler.Shape.OutputPos[j].Yaw(coupler.Yaw);
                        double top = e.Shape.TopNode != null ? e.Shape.TopY : e.Shape.Height / 2;
                        e.Pos = new Vec3(coupler.Pos.X + node.X, coupler.Pos.Y + node.Y - top, coupler.Pos.Z + node.Z);
                        e.Yaw = coupler.Yaw;
                        Link(e, coupler, AttachKind.Stack);
                        e.SelfNode = e.Shape.TopNode ?? "top";
                        e.ParentNode = coupler.Shape.OutputNodes[j];
                        e.Activation = ignition;
                        engines.Add(e);
                    }
                    if (symmetry) Notes.Add(string.Format("The {0} {1} engines are mounted under a {2}.", n, engine.Title, couplerInfo.Title));
                    return engines;
                }
                // The model does not match what the inventory announced: fall back to the surface-attached cluster.
                parts.Remove(coupler);
                tank.Children.Remove(coupler);
            }

            bool center = n == 1 || n >= 5;
            if (center)
            {
                CraftPart e = StackBelow(tank, engine);
                e.Activation = ignition;
                engines.Add(e);
            }
            int ring = center ? n - 1 : n;
            if (ring == 0) return engines;

            PartShape es = shapes.Get(engine);
            double tankR = tank.Shape.Diameter / 2, engR = es.Diameter / 2;
            double radius = Math.Max(tankR - engR, engR / Math.Sin(Math.PI / Math.Max(2, ring)));
            if (center) radius = Math.Max(radius, 2 * engR);
            double y = Bottom(tank);
            List<CraftPart> group = new List<CraftPart>();
            for (int j = 0; j < ring; j++)
            {
                double angle = baseAngle + 360.0 * j / ring;
                CraftPart e = Make(engine);
                double top = e.Shape.TopNode != null ? e.Shape.TopY : e.Shape.Height / 2;
                e.Pos = new Vec3(tank.Pos.X, y - top, tank.Pos.Z) + new Vec3(0, 0, radius).Yaw(angle);
                Link(e, tank, AttachKind.Surface);
                e.SelfNode = "srfAttach";
                e.Activation = ignition;
                group.Add(e);
                engines.Add(e);
            }
            if (symmetry) SetSymmetry(group);
            if (n > 1 && symmetry) Notes.Add(string.Format("The {0} {1} engines are attached in a cluster under the tank; a multi-coupler " +
                "(bi, tri or quad) gives a cleaner mounting.", n, engine.Title));
            return engines;
        }

        void AddBoosters(List<CraftPart> core, List<CraftPart> coreEngines, BoosterPlan b, StagePlan s)
        {
            double coreBottom = double.MaxValue;
            foreach (CraftPart p in core) coreBottom = Math.Min(coreBottom, Bottom(p));
            List<CraftPart> tanks = core.FindAll(p => p.Info.Kind == PartKind.Tank);
            if (tanks.Count == 0) tanks = core;

            PartShape decShape = b.Decoupler != null ? shapes.Get(b.Decoupler) : null;
            double decThickness = 0;
            if (decShape != null)
            {
                double h = Math.Sqrt(decShape.SrfPos.X * decShape.SrfPos.X + decShape.SrfPos.Z * decShape.SrfPos.Z);
                decThickness = Math.Max(0.1, Math.Min(0.6, 2 * h));
            }
            else Notes.Add("No radial decoupler unlocked: the boosters are attached directly and cannot be dropped.");

            // Parts of a booster, top to bottom: a solid motor alone, or tanks then engine.
            List<PartInfo> stack = new List<PartInfo>();
            bool cluster = !b.Solid && (b.EnginesEach > 1 || b.Coupler != null);
            if (b.Solid) stack.Add(b.Part);
            else
            {
                foreach (TankUse t in b.Tanks) for (int n = 0; n < t.Count; n++) stack.Add(t.Tank);
                stack.Add(b.Part);
                if (b.Coupler != null) stack.Add(b.Coupler);   // only for the height: the coupler sits above the engines
            }
            // Height of the attach point so the bottom of the booster lines up with the bottom of the core.
            PartShape topShape = shapes.Get(stack[0]);
            double drop = 0;   // from the center of the top part to the bottom of the booster
            for (int j = 0; j < stack.Count; j++)
            {
                PartShape sh = shapes.Get(stack[j]);
                if (j == 0) drop = (sh.BottomNode != null ? -sh.BottomY : sh.Height / 2);
                else drop += sh.Height;
            }
            double attachY = coreBottom + drop + topShape.SrfPos.Y;

            CraftPart host = tanks[0];
            foreach (CraftPart t in tanks)
                if (attachY <= Top(t) && attachY >= Bottom(t)) { host = t; break; }
                else if (Math.Abs(attachY - (Top(t) + Bottom(t)) / 2) < Math.Abs(attachY - (Top(host) + Bottom(host)) / 2)) host = t;
            attachY = Math.Max(Bottom(host), Math.Min(Top(host), attachY));
            double coreR = host.Shape.Diameter / 2;
            // Clearance: the booster's axis sits at the core tank's radius + decoupler + the booster's widest tank,
            // whatever the surface attach point of its top part says (adapter tanks have it on their narrow part).
            // Engines are kept apart by turning the rings (between the boosters for the core, tangential on the boosters).
            double widestCore = 0, widestBooster = 0;
            widestCore = coreR;
            foreach (PartInfo p in stack)
                if (p != b.Part || b.Solid) widestBooster = Math.Max(widestBooster, shapes.Get(p).Diameter / 2);
            List<List<CraftPart>> perBooster = new List<List<CraftPart>>();
            List<CraftPart> cones = new List<CraftPart>();
            for (int i = 0; i < b.Count; i++)
            {
                double angle = 360.0 * i / b.Count;
                Vec3 outward = new Vec3(0, 0, 1).Yaw(angle);
                Vec3 point = new Vec3(host.Pos.X, attachY, host.Pos.Z) + outward * coreR;
                List<CraftPart> mine = new List<CraftPart>();
                CraftPart mount = host;
                if (b.Decoupler != null)
                {
                    CraftPart dec = SurfaceAt(host, b.Decoupler, point, outward);
                    // Asparagus: the pair i and i + n/2 (opposite) is dropped first for i = 0.
                    int pair = b.Crossfeed ? i % (b.Count / 2) : 0;
                    dec.Activation = 1 + pair;
                    dec.Crossfeed = b.Crossfeed && FuelLine == null;
                    mine.Add(dec);
                    mount = dec;
                    point = point + outward * decThickness;
                }
                int stacked = cluster ? stack.Count - (b.Coupler != null ? 2 : 1) : stack.Count;
                CraftPart cur = SurfaceAt(mount, stack[0], point, outward);
                double radial = Vec3.Dot(new Vec3(cur.Pos.X - host.Pos.X, 0, cur.Pos.Z - host.Pos.Z), outward);
                double needed = widestCore + decThickness + widestBooster;
                if (Math.Abs(radial - needed) > 0.05) cur.Pos = cur.Pos + outward * (needed - radial);
                mine.Add(cur);
                for (int j = 1; j < stacked; j++)
                {
                    cur = StackBelow(cur, stack[j]);
                    mine.Add(cur);
                }
                if (cluster)
                {
                    // Several engines under the booster's last tank: on a multi-coupler, or in a ring.
                    CraftPart last = cur;
                    // Ring turned tangentially: the booster's engines stay clear of the core.
                    List<CraftPart> engines = AddEngines(last, b.Part, b.EnginesEach, b.Coupler, 0, false, angle + 90);
                    CraftPart coupler = last.Children.Find(c => b.Coupler != null && c.Info == b.Coupler);
                    if (coupler != null) mine.Add(coupler);
                    mine.AddRange(engines);
                }
                // The last element is the engine (or solid motor): it ignites at liftoff.
                else cur.Activation = 0;
                perBooster.Add(mine);
                CraftPart top = mine[b.Decoupler != null ? 1 : 0];
                if (b.NoseCone != null && top.Shape.TopNode != null && top.Shape.BottomNode != null)
                {
                    // Adapters first when no cone fits the booster's size, each part in symmetry with its counterparts.
                    CraftPart cap = top;
                    foreach (PartInfo a in b.NoseAdapters) { cap = StackAbove(cap, a); mine.Add(cap); }
                    cones.Add(StackAbove(cap, b.NoseCone));
                }
            }
            if (cones.Count > 0)
            {
                if (b.Crossfeed)
                    for (int g = 0; g < b.Count / 2; g++)
                    {
                        List<CraftPart> pair = new List<CraftPart>();
                        for (int i = g; i < cones.Count; i += b.Count / 2) pair.Add(cones[i]);
                        SetSymmetry(pair);
                    }
                else SetSymmetry(cones);
            }
            List<List<CraftPart>> separators = AddSeparators(perBooster, b, host);

            // Symmetry: all boosters together, or pair by pair for asparagus.
            int groups = b.Crossfeed ? b.Count / 2 : 1;
            for (int g = 0; g < groups; g++)
                for (int j = 0; j < perBooster[0].Count; j++)
                {
                    List<CraftPart> set = new List<CraftPart>();
                    for (int i = 0; i < b.Count; i++)
                        if (!b.Crossfeed || i % groups == g) set.Add(perBooster[i][j]);
                    SetSymmetry(set);
                }
            for (int g = 0; g < groups; g++)
                for (int j = 0; j < b.SeparatorsEach && separators[0].Count > j; j++)
                {
                    List<CraftPart> set = new List<CraftPart>();
                    for (int i = 0; i < b.Count; i++)
                        if (!b.Crossfeed || i % groups == g) set.Add(separators[i][j]);
                    SetSymmetry(set);
                }
            if (b.Crossfeed && FuelLine != null) AddFuelLines(perBooster, b, groups, host);
            else if (b.Crossfeed)
                Notes.Add("No fuel line unlocked: fuel transfers through the radial decouplers, and the outer pairs empty first.");
        }

        /// <summary>
        /// Separation motors: half at the top of the booster, half at the bottom, on the outer side, nozzle toward the core
        /// and slightly upward. They push the empty booster outward and backward when its decoupler fires.
        /// </summary>
        List<List<CraftPart>> AddSeparators(List<List<CraftPart>> perBooster, BoosterPlan b, CraftPart core)
        {
            List<List<CraftPart>> all = new List<List<CraftPart>>();
            foreach (List<CraftPart> unused in perBooster) all.Add(new List<CraftPart>());
            if (b.Decoupler == null) return all;
            if (b.SeparatorsEach <= 0 || b.Separator == null)
            {
                Notes.Add("No separation motor unlocked (Sepratron): dropped boosters may hit the rocket.");
                return all;
            }
            int first = 1;
            int perEnd = b.SeparatorsEach / 2;
            for (int i = 0; i < perBooster.Count; i++)
            {
                List<CraftPart> mine = perBooster[i];
                CraftPart topPart = mine[first];
                // At the bottom: the last tank (liquid booster) or the solid motor itself.
                CraftPart bottomPart = b.Solid ? mine[mine.Count - 1]
                    : mine.FindLast(p => b.Tanks.Exists(t => t.Tank == p.Info)) ?? mine[Math.Max(first, mine.Count - 2)];
                int activation = mine[0].Activation;
                Vec3 outward = new Vec3(mine[first].Pos.X - core.Pos.X, 0, mine[first].Pos.Z - core.Pos.Z).Unit;
                Vec3 toCore = outward * -1;
                for (int end = 0; end < 2; end++)
                {
                    CraftPart host = end == 0 ? topPart : bottomPart;
                    double span = Top(host) - Bottom(host);
                    double y = end == 0 ? Top(host) - Math.Min(0.6, 0.15 * span) : Bottom(host) + Math.Min(0.6, 0.15 * span);
                    for (int k = 0; k < perEnd; k++)
                    {
                        // On the face turned toward the core, on either side of the decoupler (±40°).
                        double spread = perEnd == 1 ? 0 : -40 + 80.0 * k / (perEnd - 1);
                        Vec3 normal = toCore.Yaw(spread);
                        Vec3 point = new Vec3(host.Pos.X, y, host.Pos.Z) + normal * (host.Shape.Diameter / 2);
                        CraftPart sep = SurfaceAt(host, b.Separator, point, normal);
                        sep.Tilted = SeparatorRotation(sep, normal, toCore);
                        sep.Pos = point - sep.Rotation.Rotate(sep.Shape.SrfPos);
                        sep.Activation = activation;
                        all[i].Add(sep);
                    }
                }
            }
            Notes.Add(string.Format("{0} {1} per booster push it away from the rocket on separation.", b.SeparatorsEach, b.Separator.Title));
            return all;
        }

        /// <summary>
        /// Orientation of a separation motor: laid flat against the booster as in the editor, flipped (nozzle
        /// upward) then tilted by 25° so its exhaust goes upward and toward the core. The thrust
        /// brakes the dropped booster and pushes it away from the rocket.
        /// </summary>
        static Quat SeparatorRotation(CraftPart sep, Vec3 normal, Vec3 toCore)
        {
            Quat upright = Quat.FromYaw(sep.Yaw);
            Vec3 exhaust = upright.Rotate(sep.Shape.ExhaustDir);
            // Nozzle points down in the normal pose: flip it by rotating around the normal (the attach face stays against the booster).
            Quat flip = exhaust.Y < 0 ? Axis(normal, 180) : new Quat(0, 0, 0, 1);
            Vec3 tangent = Vec3.Cross(new Vec3(0, 1, 0), normal).Unit;
            Quat best = flip * upright;
            double bestScore = double.MinValue;
            foreach (double angle in new[] { 25.0, -25.0 })
            {
                Quat q = Axis(tangent, angle) * flip * upright;
                Vec3 e = q.Rotate(sep.Shape.ExhaustDir).Unit;
                double score = Vec3.Dot(e, toCore) + e.Y;
                if (score > bestScore) { bestScore = score; best = q; }
            }
            return best;
        }

        static Quat Axis(Vec3 axis, double degrees)
        {
            Vec3 a = axis.Unit;
            double h = degrees * Math.PI / 360, sn = Math.Sin(h);
            return new Quat(a.X * sn, a.Y * sn, a.Z * sn, Math.Cos(h));
        }

        /// <summary>
        /// Asparagus: each booster of a pair feeds its neighbor in the next pair, the last pair feeds the core.
        /// The lines start from the top tank of each booster, at mid-height.
        /// </summary>
        void AddFuelLines(List<List<CraftPart>> perBooster, BoosterPlan b, int groups, CraftPart core)
        {
            int first = b.Decoupler != null ? 1 : 0;
            for (int g = 0; g < groups; g++)
            {
                List<CraftPart> lines = new List<CraftPart>();
                for (int i = 0; i < b.Count; i++)
                {
                    if (i % groups != g) continue;
                    CraftPart source = perBooster[i][first];
                    CraftPart target = g + 1 < groups ? perBooster[(i + 1) % b.Count][first] : core;
                    double y = (Top(source) + Bottom(source)) / 2;
                    Vec3 from = new Vec3(source.Pos.X, y, source.Pos.Z), to = new Vec3(target.Pos.X, y, target.Pos.Z);
                    Vec3 d = to - from;
                    double len = Math.Sqrt(d.X * d.X + d.Z * d.Z);
                    if (len < 1e-6) continue;
                    d = d * (1 / len);
                    Vec3 start = from + d * (source.Shape.Diameter / 2);
                    CraftPart line = SurfaceAt(source, FuelLine, start, d);
                    line.FuelTarget = target;
                    line.TargetPoint = to - d * (target.Shape.Diameter / 2);
                    lines.Add(line);
                }
                SetSymmetry(lines);
            }
            Notes.Add("Asparagus: fuel lines connect each booster pair to the next, then to the core.");
        }

        static void SetSymmetry(List<CraftPart> group)
        {
            if (group.Count < 2) return;
            foreach (CraftPart p in group)
                foreach (CraftPart q in group)
                    if (p != q) p.Symmetry.Add(q);
        }

        /// <summary>Each part is dropped by the first decoupler met when walking up toward the root.</summary>
        void AssignSeparation()
        {
            foreach (CraftPart p in parts)
            {
                CraftPart q = p;
                while (q != null && !(q.Info.Kind == PartKind.Decoupler && q != p)) q = q.Parent;
                p.Separation = q != null ? q.Activation : -1;
            }
        }

        /// <summary>Inverse stage in KSP's sense: the first activated stage carries the largest number.</summary>
        public int InverseStage(int activation)
        {
            return activation < 0 ? -1 : actions - 1 - activation;
        }

        public int ActionCount { get { return actions; } }
    }
}
