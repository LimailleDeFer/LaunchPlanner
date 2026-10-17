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

    /// <summary>What the planner knows about a part: everything used for the delta-v calculation.</summary>
    public class PartInfo
    {
        public string Name;
        public string Variant = "";  // contents chosen for a switchable tank (B9PartSwitch)
        public string VariantModule = "";  // moduleID of the ModuleB9PartSwitch that carries these contents
        public string Title;
        public string Source;      // GameData folder the part comes from (Squad, SquadExpansion, mod name...)
        public string Sizes;       // attach profiles, e.g. "size1, srf"
        public string Tech;
        public string Category = "";  // editor category (Coupling for decouplers)
        public double HeightM;        // distance between the top and bottom stack nodes (0 if unknown)
        public double DiameterM;      // actual width of the 3D model (0 if unknown)
        public bool NoseTop;          // the top of the model ends in a point (nose tank, booster with a cap): nothing stacks on top
        public bool Storage;
        // Landing legs: from the surface attach point down to the foot, folded and deployed (0 if unknown)
        public double LegFoldedM;
        public double LegDeployedM;
        // Parachutes: drag once fully deployed, and whether it is a drogue (opens high and fast)
        public double ChuteDrag;
        public bool Drogue;          // cargo bay or inventory slots (service tanks)

        // Multi-couplers (bi, tri, quad): one node on top, several on the bottom
        public int StackOutputs;
        public double TopDiameterM;
        public double OutputDiameterM;
        public double OutputSpacingM;  // smallest distance between two bottom nodes
        // Structural adapters (no propellant): profile of the top and bottom nodes
        public string AdapterTop = "";
        public string AdapterBottom = "";
        // Reaction wheels: smallest of the pitch and yaw torques (kN·m)
        public double TorqueKNm;
        // RCS thrusters: thrust of one nozzle (kN), number of nozzles, vacuum Isp
        public double RcsThrustKN;
        public int RcsNozzles;
        public double RcsIsp;
        public bool Unlocked;
        public PartKind Kind;
        public double Cost;
        public double DryMassT;
        public double WetMassT;
        public List<ResourceAmount> Resources = new List<ResourceAmount>();

        // Engines and boosters
        public double ThrustVacKN;
        public double ThrustAslKN;
        public double IspVac;
        public double IspAsl;
        public bool ThrottleLocked;
        public string Propellants = "";

        public const double G0 = 9.80665;

        /// <summary>Burn time at full thrust with the onboard fuel (boosters).</summary>
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
