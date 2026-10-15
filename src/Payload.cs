using System;

namespace LaunchPlanner
{
    /// <summary>La charge utile : le vaisseau actuellement ouvert dans l'éditeur (atterrisseur, module de retour…).</summary>
    public class Payload
    {
        public bool Present;
        public string Name = "";
        public int PartCount;
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
            float dry, fuel;
            ship.GetShipMass(out dry, out fuel);
            p.DryMassT = dry;
            p.MassT = dry + fuel;

            // Δv calculé par le jeu (application Δv de KSP), dans le vide.
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
