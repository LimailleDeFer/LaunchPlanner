using System;
using System.Collections.Generic;

namespace LaunchPlanner
{
    /// <summary>Lit les corps célestes chargés par KSP (y compris ceux des packs de planètes Kopernicus).</summary>
    public static class KspBodies
    {
        public static List<BodyInfo> All = new List<BodyInfo>();
        public static BodyInfo Home;

        public static void Refresh()
        {
            All.Clear();
            Home = null;
            Dictionary<CelestialBody, BodyInfo> map = new Dictionary<CelestialBody, BodyInfo>();
            List<CelestialBody> bodies = FlightGlobals.Bodies;
            if (bodies == null) return;

            foreach (CelestialBody cb in bodies)
            {
                if (cb == null) continue;
                Orbit orbit = cb.orbitDriver != null ? cb.orbit : null;
                BodyInfo b = new BodyInfo
                {
                    Name = DisplayName(cb),
                    Key = cb.bodyName,
                    Mu = cb.gravParameter,
                    Radius = cb.Radius,
                    HasAtmosphere = cb.atmosphere,
                    AtmosphereDepth = cb.atmosphereDepth,
                    PressureAtm = cb.atmospherePressureSeaLevel / 101.325,
                    RotationPeriod = cb.rotates ? cb.rotationPeriod : 0,
                    HasSurface = cb.hasSolidSurface,
                    IsStar = cb.isStar,
                    Sma = orbit != null ? orbit.semiMajorAxis : 0,
                };
                map[cb] = b;
                All.Add(b);
            }

            foreach (KeyValuePair<CelestialBody, BodyInfo> kv in map)
            {
                CelestialBody parent = kv.Key.orbitDriver != null && kv.Key.orbit != null ? kv.Key.orbit.referenceBody : null;
                BodyInfo p;
                if (parent != null && parent != kv.Key && map.TryGetValue(parent, out p)) kv.Value.Parent = p;
            }

            CelestialBody home = FlightGlobals.GetHomeBody();
            if (home != null) map.TryGetValue(home, out Home);

            // Ordre d'affichage : chaque corps suivi de ses lunes, par distance au parent.
            List<BodyInfo> ordered = new List<BodyInfo>();
            foreach (BodyInfo root in All) if (root.Parent == null) AddTree(root, ordered);
            All = ordered;
        }

        static void AddTree(BodyInfo body, List<BodyInfo> ordered)
        {
            ordered.Add(body);
            List<BodyInfo> children = new List<BodyInfo>();
            foreach (BodyInfo b in All) if (b.Parent == body) children.Add(b);
            children.Sort((a, b) => a.Sma.CompareTo(b.Sma));
            foreach (BodyInfo c in children) AddTree(c, ordered);
        }

        static string DisplayName(CelestialBody cb)
        {
            string name = cb.displayName;
            if (string.IsNullOrEmpty(name)) name = cb.bodyName;
            int caret = name.IndexOf('^');   // les noms localisés finissent par "^N"
            return caret > 0 ? name.Substring(0, caret) : name;
        }
    }
}
