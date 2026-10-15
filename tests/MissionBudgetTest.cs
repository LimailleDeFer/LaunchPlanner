// Vérifie le calcul du budget Δv avec les valeurs du système de Kerbol, à comparer à la carte communautaire.
//   mcs tests/MissionBudgetTest.cs src/MissionBudget.cs -out:build/test.exe && mono build/test.exe
using System;
using LaunchPlanner;

static class MissionBudgetTest
{
    static BodyInfo B(string n, double mu, double r, double atmo, double p, double rot, double sma, BodyInfo parent)
    {
        return new BodyInfo { Name = n, Mu = mu, Radius = r, HasAtmosphere = atmo > 0, AtmosphereDepth = atmo, PressureAtm = p,
            RotationPeriod = rot, Sma = sma, Parent = parent };
    }

    static int failures;

    static void Expect(string what, double actual, double expected, double tolerance)
    {
        bool ok = Math.Abs(actual - expected) <= tolerance;
        if (!ok) failures++;
        Console.WriteLine("{0,-4} {1,-38} {2,7:0} m/s (attendu ≈ {3:0} ± {4:0})", ok ? "OK" : "ÉCHEC", what, actual, expected, tolerance);
    }

    static int Main()
    {
        BodyInfo sun = B("Sun", 1.1723328e18, 261600000, 0, 0, 432000, 0, null);
        sun.IsStar = true;
        BodyInfo kerbin = B("Kerbin", 3.5316e12, 600000, 70000, 1, 21549.425, 13599840256, sun);
        BodyInfo mun = B("Mun", 6.5138398e10, 200000, 0, 0, 138984, 12000000, kerbin);
        BodyInfo minmus = B("Minmus", 1.7658e9, 60000, 0, 0, 40400, 47000000, kerbin);
        BodyInfo duna = B("Duna", 3.0136321e11, 320000, 50000, 0.0666, 65517.859, 20726155264, sun);
        BodyInfo eve = B("Eve", 8.1717302e12, 700000, 90000, 5, 80500, 9832684544, sun);
        BodyInfo jool = B("Jool", 2.82528e14, 6000000, 200000, 15, 36000, 68773560320, sun);
        jool.HasSurface = false;
        BodyInfo laythe = B("Laythe", 1.962e12, 500000, 50000, 0.6, 52980.879, 27184000, jool);

        double o, c, i, rc; string note;
        MissionBudget.Transfer(kerbin, mun, out o, out c, out i, out rc, out note);
        Expect("Kerbin → Mun : injection", o, 860, 30);
        Expect("Kerbin → Mun : capture", c, 310, 30);
        MissionBudget.Transfer(kerbin, minmus, out o, out c, out i, out rc, out note);
        Expect("Kerbin → Minmus : injection (sans plan)", o, 930, 40);
        Expect("Kerbin → Minmus : capture", c, 160, 40);
        MissionBudget.Transfer(kerbin, duna, out o, out c, out i, out rc, out note);
        Expect("Kerbin → Duna : éjection", o, 1060, 60);
        Expect("Kerbin → Duna : capture", c, 610, 80);
        MissionBudget.Transfer(kerbin, eve, out o, out c, out i, out rc, out note);
        Expect("Kerbin → Eve : éjection", o, 1080, 60);
        MissionBudget.Transfer(kerbin, laythe, out o, out c, out i, out rc, out note);
        Expect("Kerbin → Laythe : éjection", o, 1910, 100);

        Budget mission = MissionBudget.Compute(kerbin, mun, MissionType.LandingReturn, 0, 0);
        Expect("Mun aller-retour avec atterrissage", mission.Total, 3400 + 860 + 310 + 580 + 580 + 310, 80);

        Budget withPayload = MissionBudget.Compute(kerbin, mun, MissionType.LandingReturn, 0.1, 2000);
        Expect("Lanceur = total + marge − charge utile", withPayload.LauncherDeltaV, withPayload.TotalWithMargin - 2000, 1);

        Budget jl = MissionBudget.Compute(kerbin, jool, MissionType.Landing, 0, 0);
        Console.WriteLine("Avertissement Jool : " + string.Join(" | ", jl.Warnings.ToArray()));
        if (jl.Warnings.Count == 0) failures++;

        Console.WriteLine(failures == 0 ? "Tous les tests passent." : failures + " échec(s).");
        return failures == 0 ? 0 : 1;
    }
}
