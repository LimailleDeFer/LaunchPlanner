using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LaunchPlanner
{
    /// <summary>Lit la forme des pièces sur leur modèle chargé par le jeu (nœuds de fixation, modules).</summary>
    public class KspPartShapes : IPartShapes
    {
        readonly Dictionary<string, PartShape> cache = new Dictionary<string, PartShape>();

        public PartShape Get(PartInfo part)
        {
            PartShape shape;
            if (cache.TryGetValue(part.Name, out shape)) return shape;
            shape = new PartShape { Diameter = CraftBuilder.DiameterOf(LauncherDesigner.SizeOf(part)) };
            AvailablePart ap = PartLoader.getPartInfoByName(part.Name);
            Part prefab = ap != null ? ap.partPrefab : null;
            if (prefab != null)
            {
                double top = double.MinValue, bottom = double.MaxValue;
                foreach (AttachNode n in prefab.attachNodes)
                {
                    if (n == null || n.nodeType != AttachNode.NodeType.Stack) continue;
                    if (n.position.y > top) { top = n.position.y; shape.TopNode = n.id; }
                    if (n.position.y < bottom) { bottom = n.position.y; shape.BottomNode = n.id; }
                }
                if (shape.TopNode == shape.BottomNode) shape.BottomNode = null;   // un seul nœud : il sert de nœud du haut
                shape.TopY = shape.TopNode != null ? top : 0;
                shape.BottomY = shape.BottomNode != null ? bottom : 0;
                if (prefab.srfAttachNode != null)
                {
                    Vector3 p = prefab.srfAttachNode.position, o = prefab.srfAttachNode.orientation;
                    shape.SrfPos = new Vec3(p.x, p.y, p.z);
                    shape.SrfDir = new Vec3(o.x, o.y, o.z);
                }
                // Sens de sortie des gaz (fusées de séparation inclinées) : axe avant du point de poussée, dans le repère de la pièce.
                ModuleEngines engine = prefab.FindModuleImplementing<ModuleEngines>();
                Transform thrust = engine != null ? prefab.FindModelTransform(engine.thrustVectorTransformName) : null;
                if (thrust != null)
                {
                    Vector3 d = prefab.transform.InverseTransformDirection(thrust.forward);
                    if (d.sqrMagnitude > 0.01f) shape.ExhaustDir = new Vec3(d.x, d.y, d.z);
                }
                foreach (PartModule m in prefab.Modules)
                {
                    shape.Modules.Add(m.moduleName);
                    shape.ModuleIds.Add(m.moduleName == "ModuleB9PartSwitch" ? Refl.Str(m, "moduleID") : "");
                }
            }
            cache[part.Name] = shape;
            return shape;
        }
    }

    /// <summary>
    /// Construit la variante choisie. S'il y a une charge utile dans l'éditeur, le lanceur est fixé sous sa pièce la plus basse
    /// (nœud du bas libre) et ses étages passent avant ceux de la charge utile ; le vaisseau complet est ensuite rechargé.
    /// </summary>
    public static class CraftSpawner
    {
        public static string LastCraftPath = "";
        public static string BackupPath = "";

        /// <summary>Renvoie un message d'erreur, ou null si le lanceur est en place dans l'éditeur.</summary>
        public static string Spawn(LauncherDesign design, List<string> notes)
        {
            EditorLogic editor = EditorLogic.fetch;
            if (editor == null) return "L'éditeur n'est pas ouvert.";
            if (EditorLogic.SelectedPart != null) return "Repose d'abord la pièce que tu tiens, puis recommence.";
            if (design == null || design.Stages.Count == 0) return "Aucune variante choisie.";

            KspPartShapes shapes = new KspPartShapes();
            CraftBuilder builder = new CraftBuilder(shapes) { FuelLine = FuelLine() };
            List<CraftPart> parts = builder.Build(design);
            notes.AddRange(builder.Notes);

            ShipConstruct ship = editor.ship;
            ConfigNode result;
            if (ship != null && ship.parts != null && ship.parts.Count > 0)
            {
                string error = AttachUnder(ship, builder, parts, out result);
                if (error != null) return error;
            }
            else
            {
                result = ConfigNode.Parse(CraftWriter.Write(builder, parts, "Lanceur " + design.Variant, GameVersion()).ToText());
            }
            if (result == null) return "Lecture du lanceur généré impossible.";
            if (!Save(result)) return "Impossible d'écrire le vaisseau dans GameData/LaunchPlanner.";
            EditorLogic.LoadShipFromFile(LastCraftPath);
            return null;
        }

        /// <summary>Conduite de carburant FTX-2 si elle est débloquée.</summary>
        static PartInfo FuelLine()
        {
            AvailablePart ap = PartLoader.getPartInfoByName("fuelLine");
            if (ap == null) return null;
            if (PartInventory.SafeHasResearch() && !PartInventory.SafePurchased(ap)) return null;
            return new PartInfo { Name = ap.name, Title = ap.title, Kind = PartKind.Tank, Cost = ap.cost };
        }

        static string Key(Part p)
        {
            return p.partInfo.name + "_" + p.craftID;
        }

        /// <summary>Ajoute le lanceur sous la charge utile, dans le vaisseau sauvegardé tel que l'éditeur l'enregistre.</summary>
        static string AttachUnder(ShipConstruct ship, CraftBuilder builder, List<CraftPart> parts, out ConfigNode result)
        {
            result = null;
            // Nœud empilé libre, tourné vers le bas, le plus bas du vaisseau.
            Part host = null;
            AttachNode hostNode = null;
            Vector3 hostPoint = Vector3.zero;
            foreach (Part p in ship.parts)
                foreach (AttachNode n in p.attachNodes)
                {
                    if (n == null || n.nodeType != AttachNode.NodeType.Stack || n.attachedPart != null) continue;
                    Vector3 dir = p.transform.TransformDirection(n.orientation);
                    if (dir.y > -0.5f) continue;
                    Vector3 point = p.transform.TransformPoint(n.position);
                    if (host == null || point.y < hostPoint.y) { host = p; hostNode = n; hostPoint = point; }
                }
            if (host == null)
                return "Ta charge utile n'a pas de nœud libre en bas : retire ce qui est fixé sous sa dernière pièce, puis recommence.";

            CraftPart root = parts[0];
            double rootTop = root.Shape.TopNode != null ? root.Shape.TopY : root.Shape.Height / 2;
            Vec3 offset = new Vec3(hostPoint.x - root.Pos.X, hostPoint.y - rootTop - root.Pos.Y, hostPoint.z - root.Pos.Z);

            // Les étages de la charge utile restent les derniers ; ceux du lanceur passent avant.
            int payloadStages = 0;
            foreach (Part p in ship.parts) payloadStages = Math.Max(payloadStages, p.inverseStage + 1);

            ConfigNode shipNode = ship.SaveShip();
            Backup(shipNode);
            ConfigNode launcher = ConfigNode.Parse(CraftWriter.Write(builder, parts, ship.shipName, GameVersion(), offset, payloadStages).ToText());
            if (launcher == null) return "Lecture du lanceur généré impossible.";

            string hostKey = Key(host);
            ConfigNode hostPart = null;
            foreach (ConfigNode n in shipNode.GetNodes("PART"))
                if (n.GetValue("part") == hostKey) hostPart = n;
            if (hostPart == null) return "Pièce de la charge utile introuvable dans le vaisseau sauvegardé.";

            ConfigNode[] launcherParts = launcher.GetNodes("PART");
            ConfigNode rootNode = launcherParts[0];
            // Le découpleur du haut devient l'enfant de la pièce la plus basse de la charge utile.
            Vector3 rootWorld = new Vector3((float)(root.Pos.X + offset.X), (float)(root.Pos.Y + offset.Y), (float)(root.Pos.Z + offset.Z));
            Quaternion rootRot = Quaternion.AngleAxis((float)root.Yaw, Vector3.up);
            Vector3 local = host.transform.InverseTransformPoint(rootWorld);
            Quaternion localRot = Quaternion.Inverse(host.transform.rotation) * rootRot;
            rootNode.SetValue("attPos0", KSPUtil.WriteVector(local));
            rootNode.SetValue("attRot0", KSPUtil.WriteQuaternion(localRot));
            rootNode.AddValue("attN", (root.Shape.TopNode ?? "top") + "," + hostKey);
            hostPart.AddValue("link", root.Key);
            hostPart.AddValue("attN", hostNode.id + "," + root.Key);
            foreach (ConfigNode n in launcherParts) shipNode.AddNode(n);
            result = shipNode;
            return null;
        }

        /// <summary>Copie du vaisseau avant l'ajout du lanceur, pour revenir en arrière (vaisseau « Avant lanceur » de la partie).</summary>
        static void Backup(ConfigNode shipNode)
        {
            try
            {
                // Dans les vaisseaux de la partie : il se recharge depuis le bouton « Charger » de l'éditeur.
                string dir = Path.Combine(KSPUtil.ApplicationRootPath, "saves/" + HighLogic.SaveFolder + "/Ships/VAB");
                Directory.CreateDirectory(dir);
                BackupPath = Path.Combine(dir, "Avant lanceur.craft");
                ConfigNode copy = shipNode.CreateCopy();
                copy.SetValue("ship", "Avant lanceur", true);
                copy.Save(BackupPath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static string GameVersion()
        {
            try
            {
                return Versioning.version_major + "." + Versioning.version_minor + "." + Versioning.Revision;
            }
            catch (Exception)
            {
                return "1.12.5";
            }
        }

        /// <summary>Vaisseau complet chargé dans l'éditeur, gardé pour comprendre un problème (GameData/LaunchPlanner/dernier-lanceur.craft).</summary>
        static bool Save(ConfigNode ship)
        {
            try
            {
                string dir = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/LaunchPlanner");
                Directory.CreateDirectory(dir);
                LastCraftPath = Path.Combine(dir, "dernier-lanceur.craft");
                ship.Save(LastCraftPath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }
    }
}
