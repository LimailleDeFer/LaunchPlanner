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
    }

    public enum AttachKind { Root, Stack, Surface }

    public class CraftPart
    {
        public uint Id;
        public PartInfo Info;
        public PartShape Shape;
        public Vec3 Pos;
        public double Yaw;                  // rotation autour de la verticale, en degrés
        public CraftPart Parent;
        public AttachKind Attach = AttachKind.Root;
        public string SelfNode;             // nœud de cette pièce tourné vers le parent
        public string ParentNode;           // nœud du parent (fixation empilée)
        public readonly List<CraftPart> Children = new List<CraftPart>();
        public readonly List<CraftPart> Symmetry = new List<CraftPart>();
        public int Activation = -1;         // rang d'activation (0 = premier appui sur espace), -1 si la pièce n'a pas d'icône
        public int Separation = -1;         // rang d'activation du découpleur qui largue cette pièce
        public bool Crossfeed;              // découpleur radial des asperges : laisse passer le carburant

        public string Key { get { return Info.Name + "_" + Id; } }
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
            actions = 1 + boosterDrops + (k - 1) + (d.PayloadDecoupler != null ? 1 : 0);

            CraftPart cursor = null;
            if (d.PayloadDecoupler != null)
            {
                cursor = Make(d.PayloadDecoupler);
                cursor.Activation = actions - 1;
            }
            else Notes.Add("Aucun découpleur sous la charge utile : elle restera attachée au dernier étage.");

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
                    dec.Crossfeed = b.Crossfeed;
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
            if (b.Crossfeed)
                Notes.Add("Asperges : le transfert de carburant est activé sur les découpleurs radiaux ; les paires extérieures se vident en premier.");
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
