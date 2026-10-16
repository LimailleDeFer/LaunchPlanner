using System;
using UnityEngine;

namespace LaunchPlanner
{
    /// <summary>The payload: the vessel currently open in the editor (lander, return module...).</summary>
    public class Payload
    {
        public bool Present;
        public string Name = "";
        public int PartCount;
        public double HeightM;
        public double WidthM;
        public double MassT;
        public double DryMassT;
        public double DeltaVVac;
        public bool DeltaVKnown;

        public static Payload FromEditor()
        {
            Payload p = new Payload();
            EditorLogic editor = EditorLogic.fetch;
            ShipConstruct ship = editor != null ? editor.ship : null;
            if (ship == null || ship.parts == null || ship.parts.Count == 0) return p;

            p.Present = true;
            p.Name = ship.shipName ?? "";
            p.PartCount = ship.parts.Count;
            try
            {
                Vector3 size = ShipConstruction.CalculateCraftSize(ship);
                p.HeightM = size.y;
                p.WidthM = Math.Max(size.x, size.z);
            }
            catch (Exception) { }
            float dry, fuel;
            ship.GetShipMass(out dry, out fuel);
            p.DryMassT = dry;
            p.MassT = dry + fuel;

            // Delta-v computed by the game (KSP's delta-v app), in vacuum.
            VesselDeltaV dv = ship.vesselDeltaV;
            if (dv != null && dv.IsReady)
            {
                p.DeltaVVac = dv.TotalDeltaVVac;
                p.DeltaVKnown = true;
            }
            return p;
        }
    }
}
