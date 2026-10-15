using System;
using System.Collections.Generic;

namespace LaunchPlanner
{
    /// <summary>Ce que le constructeur doit savoir de la forme d'une pièce (lu sur le modèle de la pièce dans le jeu).</summary>
    public class PartShape
    {
        public string TopNode;          // nœud empilé du haut (null s'il n'y en a pas)
        public string BottomNode;       // nœud empilé du bas
        public double TopY, BottomY;    // hauteur de ces nœuds dans le repère de la pièce
        public Vec3 SrfPos;             // point de fixation en surface
        public Vec3 SrfDir = new Vec3(0, 0, 1);   // direction du nœud de surface
        public double Diameter = 1.25;
        public List<string> Modules = new List<string>();      // noms des modules, dans l'ordre du modèle
        public List<string> ModuleIds = new List<string>();    // moduleID (B9PartSwitch), "" sinon
        public Vec3 ExhaustDir = new Vec3(0, -1, 0);           // sortie des gaz d'un moteur, dans le repère de la pièce

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

        /// <summary>Rotation autour de l'axe vertical, dans le sens de Unity (de +Z vers +X).</summary>
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

    /// <summary>Quaternion au sens de Unity (x, y, z, w), pour les pièces qui ne sont pas seulement tournées autour de la verticale.</summary>
    public struct Quat
    {
        public double X, Y, Z, W;

        public Quat(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }

        public static Quat FromYaw(double degrees)
        {
            double a = degrees * Math.PI / 360;
            return new Quat(0, Math.Sin(a), 0, Math.Cos(a));
        }

        /// <summary>Plus petite rotation qui amène la direction `from` sur `to`.</summary>
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
        public double Yaw;                  // rotation autour de la verticale, en degrés
        public Quat? Tilted;                // rotation complète, quand la pièce n'est pas droite (fusées de séparation)
        public CraftPart Parent;
        public AttachKind Attach = AttachKind.Root;
        public string SelfNode;             // nœud de cette pièce tourné vers le parent
        public string ParentNode;           // nœud du parent (fixation empilée)
        public readonly List<CraftPart> Children = new List<CraftPart>();
        public readonly List<CraftPart> Symmetry = new List<CraftPart>();
        public int Activation = -1;         // rang d'activation (0 = premier appui sur espace), -1 si la pièce n'a pas d'icône
        public int Separation = -1;         // rang d'activation du découpleur qui largue cette pièce
        public bool Crossfeed;              // découpleur radial des asperges : laisse passer le carburant
        public CraftPart FuelTarget;        // conduite de carburant : pièce alimentée
        public Vec3 TargetPoint;            // conduite de carburant : point d'arrivée sur la pièce alimentée

        public string Key { get { return Info.Name + "_" + Id; } }

        public Quat Rotation { get { return Tilted ?? Quat.FromYaw(Yaw); } }
    }

    /// <summary>
    /// Transforme une variante de lanceur en liste de pièces placées et reliées, prête à devenir un vaisseau.
    /// La racine est le découpleur sous la charge utile (ou le premier réservoir du dernier étage), son nœud du haut
    /// reste libre pour accrocher la charge utile.
    /// </summary>
    public class CraftBuilder
    {
        readonly IPartShapes shapes;
        readonly List<CraftPart> parts = new List<CraftPart>();
        public readonly List<string> Notes = new List<string>();
        uint nextId = 100001;
        int actions;

        /// <summary>Conduite de carburant débloquée (FTX-2) pour les asperges ; null pour passer par les découpleurs.</summary>
        public PartInfo FuelLine;

        public CraftBuilder(IPartShapes shapes)
        {
            this.shapes = shapes;
        }

        public static double DiameterOf(string size)
        {
            // size0 = 0,625 m, size1 = 1,25 m, size1p5 = 1,875 m, size2 = 2,5 m…
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

        /// <summary>Accroche `child` sous `parent` (nœud du bas du parent contre nœud du haut de l'enfant).</summary>
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
        /// Fixe `child` en surface au point `point` d'une surface de normale `outward`, avec la même règle que l'éditeur :
        /// rotation = LookRotation(normale) × LookRotation(direction du nœud de surface).
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

        static double Bottom(CraftPart p) { return p.Pos.Y + (p.Shape.BottomNode != null ? p.Shape.BottomY : -p.Shape.Height / 2); }
        static double Top(CraftPart p) { return p.Pos.Y + (p.Shape.TopNode != null ? p.Shape.TopY : p.Shape.Height / 2); }

        public List<CraftPart> Build(LauncherDesign d)
        {
            parts.Clear();
            Notes.Clear();
            int k = d.Stages.Count;

            // Rangs d'activation : moteurs du premier étage et boosters, largage des boosters, puis chaque étage supérieur
            // (découpleur et allumage ensemble), enfin le largage de la charge utile.
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
                Notes.Add("Aucun découpleur sous la charge utile : elle restera attachée au dernier étage.");
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
                if (cursor == null) { cursor = Make(s.Engine); count--; }   // étage sans réservoir : le moteur sert de racine
                List<CraftPart> engines = AddEngines(cursor, s, count, ignition);
                stageParts.AddRange(engines);

                if (i == 0)
                {
                    if (boosters != null && boosters.Count > 0) AddBoosters(stageParts, engines, boosters, s);
                    break;
                }
                // Le découpleur sous l'étage : sous le moteur central, sinon sous le dernier réservoir (les moteurs l'entourent).
                CraftPart below = engines.Count == 1 && engines[0].Attach == AttachKind.Stack ? engines[0] : cursor;
                if (s.Decoupler != null)
                {
                    CraftPart dec = StackBelow(below, s.Decoupler);
                    if (below != engines[0] && engines.Count > 0) dec.Pos.Y -= engines[0].Shape.Height;
                    dec.Activation = ignition;
                    cursor = dec;
                }
                else
                {
                    Notes.Add("Aucun découpleur débloqué pour séparer l'étage " + (i + 1) + ".");
                    cursor = below;
                }
            }

            AssignSeparation();
            return parts;
        }

        CraftPart landerTank, landerEngine;

        /// <summary>
        /// Étage atterrisseur, racine du lanceur : la charge utile se fixe sur son réservoir du haut. Jambes en croix sur le
        /// réservoir du bas ; le moteur s'allume après le largage du dernier étage du lanceur.
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
                Notes.Add(string.Format("Atterrisseur : {0} × {1} (touche G pour les déployer). Vérifie dans l'éditeur qu'elles descendent plus bas que la tuyère.",
                    d.LanderLegCount, d.LanderLegs.Title));
            }
            else Notes.Add("Aucune jambe d'atterrissage débloquée : l'atterrisseur se posera sur sa tuyère.");
            return cursor;
        }

        /// <summary>Découpleur sous l'atterrisseur : sous son moteur central, sinon sous son réservoir (moteurs en couronne).</summary>
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

        /// <summary>Moteurs sous le dernier réservoir : un au centre sur le nœud, les autres en couronne fixés en surface.</summary>
        List<CraftPart> AddEngines(CraftPart tank, StagePlan s, int n, int ignition)
        {
            List<CraftPart> engines = new List<CraftPart>();
            if (tank.Info == s.Engine) { tank.Activation = ignition; engines.Add(tank); }
            if (n <= 0) return engines;

            bool center = n == 1 || n >= 5;
            if (center)
            {
                CraftPart e = StackBelow(tank, s.Engine);
                e.Activation = ignition;
                engines.Add(e);
            }
            int ring = center ? n - 1 : n;
            if (ring == 0) return engines;

            PartShape es = shapes.Get(s.Engine);
            double tankR = tank.Shape.Diameter / 2, engR = es.Diameter / 2;
            double radius = Math.Max(tankR - engR, engR / Math.Sin(Math.PI / Math.Max(2, ring)));
            if (center) radius = Math.Max(radius, 2 * engR);
            double y = Bottom(tank);
            List<CraftPart> group = new List<CraftPart>();
            for (int j = 0; j < ring; j++)
            {
                double angle = 360.0 * j / ring;
                CraftPart e = Make(s.Engine);
                double top = e.Shape.TopNode != null ? e.Shape.TopY : e.Shape.Height / 2;
                e.Pos = new Vec3(tank.Pos.X, y - top, tank.Pos.Z) + new Vec3(0, 0, radius).Yaw(angle);
                Link(e, tank, AttachKind.Surface);
                e.SelfNode = "srfAttach";
                e.Activation = ignition;
                group.Add(e);
                engines.Add(e);
            }
            SetSymmetry(group);
            if (s.EngineCount > 1) Notes.Add(string.Format("Les {0} moteurs de {1} sont fixés en grappe sous le réservoir ; un coupleur multiple " +
                "(bi, tri ou quadri) donne un montage plus propre.", s.EngineCount, s.Engine.Title));
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
            else Notes.Add("Aucun découpleur radial débloqué : les boosters sont fixés directement et ne pourront pas être largués.");

            // Pièces d'un booster, de haut en bas : propulseur à poudre seul, ou réservoirs puis moteur.
            List<PartInfo> stack = new List<PartInfo>();
            if (b.Solid) stack.Add(b.Part);
            else
            {
                foreach (TankUse t in b.Tanks) for (int n = 0; n < t.Count; n++) stack.Add(t.Tank);
                stack.Add(b.Part);
            }
            // Hauteur du point de fixation pour que le bas du booster s'aligne sur le bas du corps central.
            PartShape topShape = shapes.Get(stack[0]);
            double drop = 0;   // du centre de la pièce du haut au bas du booster
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

            List<List<CraftPart>> perBooster = new List<List<CraftPart>>();
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
                    // Asperges : la paire i et i + n/2 (opposées) est larguée en premier pour i = 0.
                    int pair = b.Crossfeed ? i % (b.Count / 2) : 0;
                    dec.Activation = 1 + pair;
                    dec.Crossfeed = b.Crossfeed && FuelLine == null;
                    mine.Add(dec);
                    mount = dec;
                    point = point + outward * decThickness;
                }
                CraftPart cur = SurfaceAt(mount, stack[0], point, outward);
                mine.Add(cur);
                for (int j = 1; j < stack.Count; j++)
                {
                    cur = StackBelow(cur, stack[j]);
                    mine.Add(cur);
                }
                // Le dernier élément est le moteur (ou le propulseur à poudre) : il s'allume au décollage.
                cur.Activation = 0;
                perBooster.Add(mine);
            }
            List<List<CraftPart>> separators = AddSeparators(perBooster, b, host);

            // Symétrie : tous les boosters ensemble, ou paire par paire pour les asperges.
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
                Notes.Add("Aucune conduite de carburant débloquée : le transfert passe par les découpleurs radiaux, les paires extérieures se vident en premier.");
        }

        /// <summary>
        /// Fusées de séparation : moitié en haut du booster, moitié en bas, côté extérieur, tuyère vers le corps central
        /// et un peu vers le haut. Elles poussent le booster vide vers l'extérieur et vers l'arrière, à l'allumage de son découpleur.
        /// </summary>
        List<List<CraftPart>> AddSeparators(List<List<CraftPart>> perBooster, BoosterPlan b, CraftPart core)
        {
            List<List<CraftPart>> all = new List<List<CraftPart>>();
            foreach (List<CraftPart> unused in perBooster) all.Add(new List<CraftPart>());
            if (b.Decoupler == null) return all;
            if (b.SeparatorsEach <= 0 || b.Separator == null)
            {
                Notes.Add("Aucune fusée de séparation débloquée (Sepratron) : les boosters largués risquent de toucher la fusée.");
                return all;
            }
            int first = 1;
            int perEnd = b.SeparatorsEach / 2;
            for (int i = 0; i < perBooster.Count; i++)
            {
                List<CraftPart> mine = perBooster[i];
                CraftPart topPart = mine[first];
                // En bas : le dernier réservoir (booster liquide) ou le propulseur à poudre lui-même.
                CraftPart bottomPart = b.Solid ? mine[mine.Count - 1] : mine[Math.Max(first, mine.Count - 2)];
                int activation = mine[0].Activation;
                Vec3 outward = new Vec3(mine[first].Pos.X - core.Pos.X, 0, mine[first].Pos.Z - core.Pos.Z).Unit;
                // Gaz vers le corps central et 20° vers le haut : poussée vers l'extérieur et légèrement freinante.
                Vec3 exhaust = outward * -Math.Cos(20 * Math.PI / 180) + new Vec3(0, Math.Sin(20 * Math.PI / 180), 0);
                for (int end = 0; end < 2; end++)
                {
                    CraftPart host = end == 0 ? topPart : bottomPart;
                    double span = Top(host) - Bottom(host);
                    double y = end == 0 ? Top(host) - Math.Min(0.6, 0.15 * span) : Bottom(host) + Math.Min(0.6, 0.15 * span);
                    for (int k = 0; k < perEnd; k++)
                    {
                        // Réparties sur la face extérieure, à ±35° au plus de l'axe.
                        double spread = perEnd == 1 ? 0 : -35 + 70.0 * k / (perEnd - 1);
                        Vec3 normal = outward.Yaw(spread);
                        Vec3 point = new Vec3(host.Pos.X, y, host.Pos.Z) + normal * (host.Shape.Diameter / 2);
                        CraftPart sep = SurfaceAt(host, b.Separator, point, normal);
                        Quat upright = Quat.FromYaw(sep.Yaw);
                        Quat rot = Quat.FromTo(upright.Rotate(sep.Shape.ExhaustDir), exhaust) * upright;
                        sep.Tilted = rot;
                        sep.Pos = point - rot.Rotate(sep.Shape.SrfPos);
                        sep.Activation = activation;
                        all[i].Add(sep);
                    }
                }
            }
            Notes.Add(string.Format("{0} {1} par booster l'écartent de la fusée au largage.", b.SeparatorsEach, b.Separator.Title));
            return all;
        }

        /// <summary>
        /// Asperges : chaque booster d'une paire alimente son voisin de la paire suivante, la dernière paire alimente le corps central.
        /// Les conduites partent du réservoir du haut de chaque booster, à mi-hauteur.
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
            Notes.Add("Asperges : des conduites de carburant relient chaque paire de boosters à la suivante, puis au corps central.");
        }

        static void SetSymmetry(List<CraftPart> group)
        {
            if (group.Count < 2) return;
            foreach (CraftPart p in group)
                foreach (CraftPart q in group)
                    if (p != q) p.Symmetry.Add(q);
        }

        /// <summary>Chaque pièce est larguée par le premier découpleur rencontré en remontant vers la racine.</summary>
        void AssignSeparation()
        {
            foreach (CraftPart p in parts)
            {
                CraftPart q = p;
                while (q != null && !(q.Info.Kind == PartKind.Decoupler && q != p)) q = q.Parent;
                p.Separation = q != null ? q.Activation : -1;
            }
        }

        /// <summary>Étage inverse au sens de KSP : le premier étage activé porte le plus grand numéro.</summary>
        public int InverseStage(int activation)
        {
            return activation < 0 ? -1 : actions - 1 - activation;
        }

        public int ActionCount { get { return actions; } }
    }
}
