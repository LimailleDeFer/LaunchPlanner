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

    /// <summary>Construit la variante choisie et la met « en main » dans l'éditeur, comme un sous-assemblage.</summary>
    public static class CraftSpawner
    {
        public static string LastCraftPath = "";

        /// <summary>Renvoie un message d'erreur, ou null si le lanceur est en main dans l'éditeur.</summary>
        public static string Spawn(LauncherDesign design, List<string> notes)
        {
            EditorLogic editor = EditorLogic.fetch;
            if (editor == null) return "L'éditeur n'est pas ouvert.";
            if (EditorLogic.SelectedPart != null) return "Repose d'abord la pièce que tu tiens, puis recommence.";
            if (design == null || design.Stages.Count == 0) return "Aucune variante choisie.";

            CraftBuilder builder = new CraftBuilder(new KspPartShapes());
            List<CraftPart> parts = builder.Build(design);
            notes.AddRange(builder.Notes);
            CraftNode craft = CraftWriter.Write(builder, parts, "Lanceur " + design.Variant, GameVersion());
            string text = craft.ToText();
            // On relit le fichier écrit, exactement comme le jeu charge un .craft.
            ConfigNode node = SaveCopy(text) ? ConfigNode.Load(LastCraftPath) : ConfigNode.Parse(text);
            if (node == null) return "Lecture du lanceur généré impossible.";
            ShipConstruct construct = new ShipConstruct();
            if (!construct.LoadShip(node)) return "Le jeu n'a pas pu assembler le lanceur (détails dans KSP.log).";
            editor.SpawnConstruct(construct);
            return null;
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

        /// <summary>Copie du dernier lanceur généré, utile pour comprendre un problème (GameData/LaunchPlanner/dernier-lanceur.craft).</summary>
        static bool SaveCopy(string text)
        {
            try
            {
                string dir = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/LaunchPlanner");
                Directory.CreateDirectory(dir);
                LastCraftPath = Path.Combine(dir, "dernier-lanceur.craft");
                File.WriteAllText(LastCraftPath, text);
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
