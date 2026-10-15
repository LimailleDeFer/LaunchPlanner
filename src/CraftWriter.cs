using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LaunchPlanner
{
    /// <summary>Nœud de configuration indépendant du jeu (même structure que ConfigNode), pour écrire un vaisseau.</summary>
    public class CraftNode
    {
        public string Name;
        public readonly List<KeyValuePair<string, string>> Values = new List<KeyValuePair<string, string>>();
        public readonly List<CraftNode> Nodes = new List<CraftNode>();

        public CraftNode(string name) { Name = name; }

        public CraftNode Add(string key, string value)
        {
            Values.Add(new KeyValuePair<string, string>(key, value));
            return this;
        }

        public CraftNode AddNode(string name)
        {
            CraftNode n = new CraftNode(name);
            Nodes.Add(n);
            return n;
        }

        /// <summary>Texte au format des fichiers .craft (le nœud racine n'est pas entouré d'accolades).</summary>
        public string ToText()
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, 0, false);
            return sb.ToString();
        }

        void Write(StringBuilder sb, int depth, bool braces)
        {
            string pad = new string('\t', depth);
            string inner = braces ? pad + "\t" : pad;
            if (braces) sb.Append(pad).Append(Name).Append('\n').Append(pad).Append("{\n");
            foreach (KeyValuePair<string, string> v in Values) sb.Append(inner).Append(v.Key).Append(" = ").Append(v.Value).Append('\n');
            foreach (CraftNode n in Nodes) n.Write(sb, braces ? depth + 1 : depth, true);
            if (braces) sb.Append(pad).Append("}\n");
        }
    }

    /// <summary>Écrit les pièces placées par CraftBuilder au format d'un vaisseau KSP.</summary>
    public static class CraftWriter
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string F(double v) { return Math.Round(v, 5).ToString("0.#####", Inv); }
        static string V(Vec3 v) { return F(v.X) + "," + F(v.Y) + "," + F(v.Z); }

        static string Q(double yawDegrees)
        {
            double a = yawDegrees * Math.PI / 360;
            return "0," + F(Math.Sin(a)) + ",0," + F(Math.Cos(a));
        }

        public static CraftNode Write(CraftBuilder builder, List<CraftPart> parts, string shipName, string version)
        {
            Random random = new Random();
            CraftNode ship = new CraftNode("ship");
            ship.Add("ship", shipName)
                .Add("version", version)
                .Add("description", "Lanceur proposé par LaunchPlanner")
                .Add("type", "VAB")
                .Add("size", "1,1,1")
                .Add("persistentId", ((uint)random.Next(1, int.MaxValue)).ToString(Inv))
                .Add("rot", "0,0,0,1");

            foreach (CraftPart p in parts)
            {
                CraftNode n = ship.AddNode("PART");
                Vec3 local = p.Parent == null ? p.Pos : (p.Pos - p.Parent.Pos).Yaw(-p.Parent.Yaw);
                double localYaw = p.Parent == null ? p.Yaw : p.Yaw - p.Parent.Yaw;
                int stage = builder.InverseStage(p.Activation);
                int sep = builder.InverseStage(p.Separation);
                n.Add("part", p.Key)
                    .Add("partName", "Part")
                    .Add("persistentId", ((uint)random.Next(1, int.MaxValue)).ToString(Inv))
                    .Add("pos", V(p.Pos))
                    .Add("attPos", "0,0,0")
                    .Add("attPos0", V(local))
                    .Add("rot", Q(p.Yaw))
                    .Add("attRot", "0,0,0,1")
                    .Add("attRot0", Q(localYaw))
                    .Add("mir", "1,1,1")
                    .Add("symMethod", "Radial")
                    .Add("autostrutMode", "Off")
                    .Add("rigidAttachment", "False")
                    .Add("istg", (stage >= 0 ? stage : Math.Max(0, sep)).ToString(Inv))
                    .Add("resPri", "0")
                    .Add("dstg", (stage >= 0 ? stage : Math.Max(0, sep)).ToString(Inv))
                    .Add("sidx", stage >= 0 ? StageIndex(parts, p).ToString(Inv) : "-1")
                    .Add("sqor", stage >= 0 ? stage.ToString(Inv) : "-1")
                    .Add("sepI", Math.Max(0, sep).ToString(Inv))
                    .Add("attm", p.Attach == AttachKind.Surface ? "1" : "0")
                    .Add("sameVesselCollision", "False")
                    .Add("modCost", "0")
                    .Add("modMass", "0")
                    .Add("modSize", "0,0,0");
                foreach (CraftPart c in p.Children) n.Add("link", c.Key);
                foreach (CraftPart q in p.Symmetry) n.Add("sym", q.Key);
                if (p.Attach == AttachKind.Stack) n.Add("attN", p.SelfNode + "," + p.Parent.Key);
                foreach (CraftPart c in p.Children)
                    if (c.Attach == AttachKind.Stack) n.Add("attN", c.ParentNode + "," + c.Key);
                if (p.Attach == AttachKind.Surface) n.Add("srfN", "srfAttach," + p.Parent.Key);

                for (int i = 0; i < p.Shape.Modules.Count; i++)
                {
                    string module = p.Shape.Modules[i];
                    CraftNode m = n.AddNode("MODULE").Add("name", module);
                    string id = i < p.Shape.ModuleIds.Count ? p.Shape.ModuleIds[i] : "";
                    if (module == "ModuleB9PartSwitch")
                    {
                        m.Add("moduleID", id);
                        if (p.Info.Variant.Length > 0 && id == p.Info.VariantModule) m.Add("currentSubtype", p.Info.Variant);
                    }
                    if (module == "ModuleToggleCrossfeed" && p.Crossfeed) m.Add("crossfeedStatus", "True");
                }
                // Réservoirs commutables : on écrit le contenu choisi.
                if (p.Info.Variant.Length > 0)
                    foreach (ResourceAmount r in p.Info.Resources)
                        n.AddNode("RESOURCE")
                            .Add("name", r.Name)
                            .Add("amount", F(r.Amount))
                            .Add("maxAmount", F(r.Amount))
                            .Add("flowState", "True")
                            .Add("isTweakable", "True")
                            .Add("hideFlow", "False")
                            .Add("isVisible", "True")
                            .Add("flowMode", "Both");
            }
            return ship;
        }

        static int StageIndex(List<CraftPart> parts, CraftPart part)
        {
            int index = 0;
            foreach (CraftPart p in parts)
            {
                if (p == part) return index;
                if (p.Activation == part.Activation) index++;
            }
            return index;
        }
    }
}
