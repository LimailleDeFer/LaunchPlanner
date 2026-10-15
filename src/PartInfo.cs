using System.Collections.Generic;

namespace LaunchPlanner
{
    public enum PartKind { Engine, Booster, Tank, Decoupler }

    public class ResourceAmount
    {
        public string Name;
        public double Amount;
        public double MassT;
    }

    /// <summary>Ce que le planificateur sait d'une pièce : tout ce qui sert au calcul du Δv.</summary>
    public class PartInfo
    {
        public string Name;
        public string Variant = "";  // contenu choisi pour un réservoir commutable (B9PartSwitch)
        public string Title;
        public string Source;      // dossier de GameData d'où vient la pièce (Squad, SquadExpansion, nom du mod…)
        public string Sizes;       // profils de fixation, ex. "size1, srf"
        public string Tech;
        public bool Unlocked;
        public PartKind Kind;
        public double Cost;
        public double DryMassT;
        public double WetMassT;
        public List<ResourceAmount> Resources = new List<ResourceAmount>();

        // Moteurs et boosters
        public double ThrustVacKN;
        public double ThrustAslKN;
        public double IspVac;
        public double IspAsl;
        public bool ThrottleLocked;
        public string Propellants = "";

        public const double G0 = 9.80665;

        /// <summary>Durée de combustion à pleine poussée avec le carburant embarqué (boosters).</summary>
        public double BurnTimeS
        {
            get
            {
                if (IspVac <= 0 || ThrustVacKN <= 0) return 0;
                double massFlowT = ThrustVacKN / (IspVac * G0); // t/s
                return (WetMassT - DryMassT) / massFlowT;
            }
        }
    }
}
