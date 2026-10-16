using System;
using System.Collections.Generic;
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
        public int OldLauncherParts;
        public double TorqueKNm;       // its own reaction wheels
        public double RcsKN;           // its own RCS: thrust of all nozzles together   // parts of a launcher generated earlier, left out of the payload

        public static Payload FromEditor()
        {
            Payload p = new Payload();
            EditorLogic editor = EditorLogic.fetch;
            ShipConstruct ship = editor != null ? editor.ship : null;
            if (ship == null || ship.parts == null || ship.parts.Count == 0) return p;

            p.Present = true;
            p.Name = ship.shipName ?? "";

            // A launcher generated earlier and still attached is not part of the payload.
            HashSet<uint> launcher = CraftSpawner.LauncherIds();
            List<Part> mine = ship.parts.FindAll(x => !CraftSpawner.OldLauncher(x, launcher));
            p.OldLauncherParts = ship.parts.Count - mine.Count;
            if (mine.Count == 0) { mine = ship.parts; p.OldLauncherParts = 0; }
            p.PartCount = mine.Count;
            foreach (Part x in mine)
                foreach (PartModule m in x.Modules)
                {
                    ModuleReactionWheel w = m as ModuleReactionWheel;
                    if (w != null) p.TorqueKNm += Math.Min(w.PitchTorque, w.YawTorque);
                    ModuleRCS r = m as ModuleRCS;
                    if (r != null && !r.isJustForShow)
                        p.RcsKN += r.thrusterPower * Math.Max(1, r.thrusterTransforms != null && r.thrusterTransforms.Count > 0
                            ? r.thrusterTransforms.Count : x.FindModelTransforms(r.thrusterTransformName).Length);
                }

            if (p.OldLauncherParts == 0)
            {
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
            }
            else
            {
                Bounds? box = null;
                foreach (Part x in mine)
                {
                    p.DryMassT += x.mass;
                    p.MassT += x.mass + x.GetResourceMass();
                    try
                    {
                        Bounds b = PartGeometryUtil.GetPartRendererBound(x);
                        if (box == null) box = b;
                        else { Bounds u = box.Value; u.Encapsulate(b); box = u; }
                    }
                    catch (Exception) { }
                }
                if (box != null)
                {
                    p.HeightM = box.Value.size.y;
                    p.WidthM = Math.Max(box.Value.size.x, box.Value.size.z);
                }
                // The game's delta-v covers the whole vessel, old launcher included: not usable.
                return p;
            }

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
