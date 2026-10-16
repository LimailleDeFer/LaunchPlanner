using System.Collections.Generic;

namespace LaunchPlanner
{
    /// <summary>Unlocked landing legs, for the lander stage.</summary>
    public static class LandingLegs
    {
        public static List<PartInfo> Unlocked()
        {
            List<PartInfo> legs = new List<PartInfo>();
            if (PartLoader.LoadedPartsList == null) return legs;
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                Part prefab = ap != null ? ap.partPrefab : null;
                if (prefab == null || prefab.srfAttachNode == null) continue;
                ModuleWheelBase wheel = prefab.FindModuleImplementing<ModuleWheelBase>();
                if (wheel == null || wheel.wheelType != WheelType.LEG) continue;
                if (PartInventory.SafeHasResearch() && !PartInventory.SafePurchased(ap)) continue;
                legs.Add(new PartInfo
                {
                    Name = ap.name,
                    Title = string.IsNullOrEmpty(ap.title) ? ap.name : ap.title,
                    Kind = PartKind.Tank,
                    Unlocked = true,
                    DryMassT = prefab.mass,
                    Cost = ap.cost,
                });
            }
            return legs;
        }
    }
}
