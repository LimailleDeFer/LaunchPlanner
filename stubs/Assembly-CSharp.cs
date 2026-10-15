// Stub de compilation uniquement : la petite partie de l'API de KSP 1.12 utilisée par le mod.
// Les membres dont la forme exacte (champ ou propriété) est incertaine ne sont pas déclarés
// ici : le mod les lit par réflexion (voir src/Refl.cs).
using System;
using System.Collections.Generic;
using UnityEngine;

public delegate void Callback();

[AttributeUsage(AttributeTargets.Class)]
public class KSPAddon : Attribute
{
    public enum Startup
    {
        Instantly = -2,
        EveryScene = -1,
        MainMenu = 2,
        Settings = 3,
        Credits = 4,
        SpaceCentre = 5,
        EditorAny = 6,
        Flight = 7,
        TrackingStation = 8,
        PSystemSpawn = 9,
    }

    public KSPAddon(Startup startup, bool once) { }
}

namespace KSP.UI.Screens
{
    public class ApplicationLauncherButton : MonoBehaviour
    {
        public void SetFalse(bool makeCall) { }
    }

    public class ApplicationLauncher : MonoBehaviour
    {
        [Flags]
        public enum AppScenes
        {
            NEVER = 0,
            SPACECENTER = 1,
            FLIGHT = 2,
            MAPVIEW = 4,
            VAB = 8,
            SPH = 16,
            TRACKSTATION = 32,
        }

        public ApplicationLauncherButton AddModApplication(Callback onTrue, Callback onFalse, Callback onHover, Callback onHoverOut,
            Callback onEnable, Callback onDisable, AppScenes visibleInScenes, Texture texture) { return null; }

        public void RemoveModApplication(ApplicationLauncherButton button) { }
    }
}

public class ConfigNode
{
    public ConfigNode[] GetNodes(string name) { return null; }
    public string GetValue(string name) { return null; }
}

public class PartLoader : MonoBehaviour { }
public class PartResourceLibrary { }

public class AvailablePart
{
    public string name;
    public string title;
    public Part partPrefab;
}

public class Part : MonoBehaviour
{
    public List<T> FindModulesImplementing<T>() where T : class { return null; }
}

public class PartModule : MonoBehaviour { }

public class FloatCurve
{
    public float Evaluate(float time) { return 0; }
}

public class Propellant
{
    public string name;
    public float ratio;
}

public class ModuleEngines : PartModule
{
    public string engineID;
    public float maxThrust;
    public float minThrust;
    public bool throttleLocked;
    public FloatCurve atmosphereCurve;
    public List<Propellant> propellants;
}

public class ResearchAndDevelopment : MonoBehaviour
{
    public static bool PartModelPurchased(AvailablePart ap) { return true; }
}
