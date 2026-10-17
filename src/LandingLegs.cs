using System;
using System.Collections.Generic;

namespace LaunchPlanner
{
    /// <summary>Unlocked parts for the lander stage: landing legs, parachutes, heat shields.</summary>
    public static class LandingLegs
    {
        public static List<PartInfo> Unlocked()
        {
            List<PartInfo> legs = new List<PartInfo>();
            foreach (AvailablePart ap in Usable())
            {
                Part prefab = ap.partPrefab;
                if (prefab.srfAttachNode == null) continue;
                ModuleWheelBase wheel = prefab.FindModuleImplementing<ModuleWheelBase>();
                if (wheel == null || wheel.wheelType != WheelType.LEG) continue;
                PartInfo leg = Info(ap);
                MeasureLeg(prefab, leg);
                legs.Add(leg);
            }
            return legs;
        }

        /// <summary>
        /// Radial parachutes. A drogue opens high and fast (its default deployment altitude is above the main
        /// chutes'); ChuteDrag is the fully deployed drag that sizes how many are needed.
        /// </summary>
        public static List<PartInfo> Parachutes()
        {
            List<PartInfo> chutes = new List<PartInfo>();
            foreach (AvailablePart ap in Usable())
            {
                Part prefab = ap.partPrefab;
                if (prefab.srfAttachNode == null || !(Refl.Str(ap, "bulkheadProfiles") ?? "").Contains("srf")) continue;
                ModuleParachute chute = prefab.FindModuleImplementing<ModuleParachute>();
                if (chute == null) continue;
                PartInfo p = Info(ap);
                p.ChuteDrag = Refl.Num(chute, "fullyDeployedDrag");
                p.Drogue = Refl.Num(chute, "deployAltitude") > 1500 || ap.name.IndexOf("drogue", StringComparison.OrdinalIgnoreCase) >= 0;
                if (p.ChuteDrag > 0) chutes.Add(p);
            }
            return chutes;
        }

        /// <summary>Rigid heat shields that stack under a part and jettison themselves (ablator and decoupler).</summary>
        public static List<PartInfo> HeatShields()
        {
            List<PartInfo> shields = new List<PartInfo>();
            foreach (AvailablePart ap in Usable())
            {
                Part prefab = ap.partPrefab;
                bool ablator = false, decoupler = false, animated = false;
                foreach (PartModule m in prefab.Modules)
                {
                    if (m == null) continue;
                    if (m.moduleName == "ModuleAblator") ablator = true;
                    if (m.moduleName == "ModuleDecouple") decoupler = true;
                    if (m.moduleName == "ModuleAnimateGeneric") animated = true;   // inflatable shields
                }
                if (!ablator || !decoupler || animated || PartInventory.StackHeight(prefab) <= 0) continue;
                PartInfo p = Info(ap);
                p.Kind = PartKind.Decoupler;
                p.Sizes = Refl.Str(ap, "bulkheadProfiles");
                p.DiameterM = PartInventory.ModelWidth(prefab);
                p.HeightM = PartInventory.StackHeight(prefab);
                p.WetMassT = p.DryMassT;
                foreach (PartResource r in prefab.Resources)
                    if (r != null && r.info != null) p.WetMassT += r.amount * r.info.density;
                shields.Add(p);
            }
            return shields;
        }

        /// <summary>
        /// Reach of the leg below its attach point, folded and deployed, from the game's drag cubes (one box per state,
        /// in the part's frame): more reliable than the meshes, which are modelled in one pose only.
        /// </summary>
        static void MeasureLeg(Part prefab, PartInfo leg)
        {
            try
            {
                if (prefab.DragCubes == null || prefab.DragCubes.Cubes == null) return;
                double attach = prefab.srfAttachNode.position.y;
                DragCube folded = null, deployed = null;
                foreach (DragCube c in prefab.DragCubes.Cubes)
                {
                    if (c == null) continue;
                    string n = (c.Name ?? "").ToLowerInvariant();
                    if (n.Contains("retract") || n.Contains("stow") || n.Contains("fold")) folded = c;
                    else if (n.Contains("deploy") || n.Contains("extend")) deployed = c;
                }
                if (folded != null) leg.LegFoldedM = Math.Max(0, attach - (folded.Center.y - folded.Size.y / 2));
                if (deployed != null) leg.LegDeployedM = Math.Max(0, attach - (deployed.Center.y - deployed.Size.y / 2));
                UnityEngine.Debug.Log(string.Format("[LaunchPlanner] leg {0}: folded {1:0.00} m, deployed {2:0.00} m below the attach point",
                    leg.Name, leg.LegFoldedM, leg.LegDeployedM));
            }
            catch (Exception) { }
        }

        static IEnumerable<AvailablePart> Usable()
        {
            if (PartLoader.LoadedPartsList == null) yield break;
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                if (ap == null || ap.partPrefab == null) continue;
                if (PartInventory.SafeHasResearch() && !PartInventory.SafePurchased(ap)) continue;
                yield return ap;
            }
        }

        static PartInfo Info(AvailablePart ap)
        {
            return new PartInfo
            {
                Name = ap.name,
                Title = string.IsNullOrEmpty(ap.title) ? ap.name : ap.title,
                Kind = PartKind.Tank,
                Unlocked = true,
                DryMassT = ap.partPrefab.mass,
                WetMassT = ap.partPrefab.mass,
                Cost = ap.cost,
            };
        }
    }
}
