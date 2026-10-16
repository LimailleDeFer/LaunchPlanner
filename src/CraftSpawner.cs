using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LaunchPlanner
{
    /// <summary>Reads the shape of parts from their model loaded by the game (attach nodes, modules).</summary>
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
                if (shape.TopNode == shape.BottomNode) shape.BottomNode = null;   // single node: it serves as the top node
                // Multi-coupler: several nodes at the same level at the bottom.
                foreach (AttachNode n in prefab.attachNodes)
                {
                    if (n == null || n.nodeType != AttachNode.NodeType.Stack || n.id == shape.TopNode) continue;
                    if (Math.Abs(n.position.y - bottom) > 0.05 || n.orientation.y > -0.5f) continue;
                    shape.OutputNodes.Add(n.id);
                    shape.OutputPos.Add(new Vec3(n.position.x, n.position.y, n.position.z));
                }
                if (shape.OutputNodes.Count < 2) { shape.OutputNodes.Clear(); shape.OutputPos.Clear(); }
                shape.TopY = shape.TopNode != null ? top : 0;
                shape.BottomY = shape.BottomNode != null ? bottom : 0;
                if (prefab.srfAttachNode != null)
                {
                    Vector3 p = prefab.srfAttachNode.position, o = prefab.srfAttachNode.orientation;
                    shape.SrfPos = new Vec3(p.x, p.y, p.z);
                    shape.SrfDir = new Vec3(o.x, o.y, o.z);
                }
                // Exhaust direction (angled separation motors): forward axis of the thrust transform, in the part's frame.
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
    /// Builds the chosen variant. If there is a payload in the editor, the launcher is attached under its lowest part
    /// (free bottom node) and its stages come before those of the payload; the complete vessel is then reloaded.
    /// </summary>
    public static class CraftSpawner
    {
        public static string LastCraftPath = "";
        public static string BackupPath = "";

        /// <summary>Returns an error message, or null if the launcher is in place in the editor.</summary>
        public static string Spawn(LauncherDesign design, List<string> notes)
        {
            EditorLogic editor = EditorLogic.fetch;
            if (editor == null) return "The editor is not open.";
            if (EditorLogic.SelectedPart != null) return "Put down the part you are holding first, then try again.";
            if (design == null || design.Stages.Count == 0) return "No variant selected.";

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
                result = ConfigNode.Parse(CraftWriter.Write(builder, parts, "Launcher " + design.Variant, GameVersion()).ToText());
            }
            if (result == null) return "Cannot read the generated launcher.";
            if (!Save(result)) return "Cannot write the vessel to GameData/LaunchPlanner.";
            EditorLogic.LoadShipFromFile(LastCraftPath);
            return null;
        }

        /// <summary>FTX-2 fuel line if unlocked.</summary>
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

        /// <summary>Adds the launcher under the payload, in the saved vessel as the editor records it.</summary>
        static string AttachUnder(ShipConstruct ship, CraftBuilder builder, List<CraftPart> parts, out ConfigNode result)
        {
            result = null;
            // Free stack node, facing down, the lowest on the vessel.
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
                return "Your payload has no free node at the bottom: remove whatever is attached under its last part, then try again.";

            CraftPart root = parts[0];
            double rootTop = root.Shape.TopNode != null ? root.Shape.TopY : root.Shape.Height / 2;
            Vec3 offset = new Vec3(hostPoint.x - root.Pos.X, hostPoint.y - rootTop - root.Pos.Y, hostPoint.z - root.Pos.Z);

            // The payload's stages stay last; the launcher's come before them.
            int payloadStages = 0;
            foreach (Part p in ship.parts) payloadStages = Math.Max(payloadStages, p.inverseStage + 1);

            ConfigNode shipNode = ship.SaveShip();
            Backup(shipNode);
            ConfigNode launcher = ConfigNode.Parse(CraftWriter.Write(builder, parts, ship.shipName, GameVersion(), offset, payloadStages).ToText());
            if (launcher == null) return "Cannot read the generated launcher.";

            string hostKey = Key(host);
            ConfigNode hostPart = null;
            foreach (ConfigNode n in shipNode.GetNodes("PART"))
                if (n.GetValue("part") == hostKey) hostPart = n;
            if (hostPart == null) return "Payload part not found in the saved vessel.";

            ConfigNode[] launcherParts = launcher.GetNodes("PART");
            ConfigNode rootNode = launcherParts[0];
            // The top decoupler becomes a child of the payload's lowest part.
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

        /// <summary>Copy of the vessel before the launcher is added, to go back (the "Before launcher" vessel of the save).</summary>
        static void Backup(ConfigNode shipNode)
        {
            try
            {
                // In the save's vessels: it can be reloaded from the editor's "Load" button.
                string dir = Path.Combine(KSPUtil.ApplicationRootPath, "saves/" + HighLogic.SaveFolder + "/Ships/VAB");
                Directory.CreateDirectory(dir);
                BackupPath = Path.Combine(dir, "Before launcher.craft");
                ConfigNode copy = shipNode.CreateCopy();
                copy.SetValue("ship", "Before launcher", true);
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

        /// <summary>Complete vessel loaded in the editor, kept to understand a problem (GameData/LaunchPlanner/last-launcher.craft).</summary>
        static bool Save(ConfigNode ship)
        {
            try
            {
                string dir = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/LaunchPlanner");
                Directory.CreateDirectory(dir);
                LastCraftPath = Path.Combine(dir, "last-launcher.craft");
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
