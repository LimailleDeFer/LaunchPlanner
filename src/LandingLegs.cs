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
                legs.Add(Info(ap));
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
