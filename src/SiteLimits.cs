using System;
using UnityEngine;

namespace LaunchPlanner
{
    /// <summary>VAB limits (part count) and launch pad limits (size, mass) according to the building levels.</summary>
    public static class SiteLimits
    {
        public static string Describe = "";

        public static void Apply(DesignRequest req)
        {
            Describe = "";
            try
            {
                GameVariables vars = GameVariables.Instance;
                if (vars != null)
                {
                    // In sandbox, buildings are at maximum level: no limit.
                    float vab = ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.VehicleAssemblyBuilding);
                    float pad = ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.LaunchPad);
                    int parts = vars.GetPartCountLimit(vab, true);
                    Vector3 size = vars.GetCraftSizeLimit(pad, true);
                    float mass = vars.GetCraftMassLimit(pad, true);
                    if (parts > 0 && parts < int.MaxValue) req.MaxParts = parts;
                    if (size.y > 0 && size.y < 1e6f) req.MaxHeight = size.y;
                    float width = Math.Min(size.x, size.z);
                    if (width > 0 && width < 1e6f) req.MaxWidth = width;
                    if (mass > 0 && mass < 1e6f) req.MaxMass = mass;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            // Volume in which the editor allows building.
            try
            {
                EditorBounds bounds = EditorBounds.Instance;
                if (bounds != null)
                {
                    Vector3 s = bounds.constructionBounds.size;
                    if (s.y > 1 && (req.MaxHeight <= 0 || s.y < req.MaxHeight)) req.MaxHeight = s.y;
                    float w = Math.Min(s.x, s.z);
                    if (w > 1 && (req.MaxWidth <= 0 || w < req.MaxWidth)) req.MaxWidth = w;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            System.Collections.Generic.List<string> parts2 = new System.Collections.Generic.List<string>();
            if (req.MaxHeight > 0) parts2.Add(string.Format("height {0:0.#} m", req.MaxHeight));
            if (req.MaxWidth > 0) parts2.Add(string.Format("width {0:0.#} m", req.MaxWidth));
            if (req.MaxParts > 0) parts2.Add(req.MaxParts + " parts");
            if (req.MaxMass > 0) parts2.Add(string.Format("{0:0.#} t", req.MaxMass));
            Describe = parts2.Count > 0 ? string.Join(" · ", parts2.ToArray()) : "none";
        }
    }
}
