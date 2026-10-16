# LaunchPlanner: launcher planner for KSP 1.12.5

A mod for Kerbal Space Program 1.12.5 (with or without Making History and Breaking Ground) that designs a launcher
based on your payload and mission goal (stages, engines, fuel, boosters), using only unlocked parts, including those
from other mods, and then builds it in the VAB under your payload.

## Progress

1. **Parts inventory (done)**: the "Available parts" tab lists the loaded engines, boosters, tanks
   (including switchable B9PartSwitch / CryoTanks tanks) and decouplers, with thrust, Isp,
   masses and fuel.
2. **Mission delta-v budget (done)**: the "Mission" tab. Pick the destination and the mission type;
   the vessel open in the editor is used as the payload. The launcher always takes it to orbit around the
   destination; the payload's own delta-v is only used for what it does on site (landing, ascent,
   return), never for the trip. A checkbox per phase lets you choose what the payload does yourself.
3. **Launcher recommendation (done)**: the "Launchers" tab. Five variants side by side (SSTO, two stages,
   solid boosters, liquid boosters, asparagus), each with its exact parts, mass, cost,
   delta-v and TWR per stage.
4. **Automatic construction in the VAB (done)**: the "Build in the editor" button assembles the chosen variant
   (tanks, engines, decouplers, boosters, staging) and attaches it under the payload.

## Installation

1. Download `LaunchPlanner.zip`.
2. Extract it into the game folder: you should get `Kerbal Space Program/GameData/LaunchPlanner/Plugins/LaunchPlanner.dll`.
3. Start KSP, open a save and enter the VAB or SPH.
4. Click the rocket-shaped button in the toolbar at the bottom right.

## Usage (step 2)

1. In the VAB, load the vessel that will be the payload (for example the lander and its return module).
2. Open the window with the rocket button, "Mission" tab.
3. Pick the destination in the list on the left and the mission type (orbit, landing, with or without return).
4. The table details each phase with its delta-v and shows whether it is handled by the payload or by the
   launcher. At the bottom, it shows what the launcher must provide.

The payload's own delta-v comes from the game's calculation (in vacuum). You can also enter it by hand.
The budget is an estimate based on Hohmann transfers, close to the community delta-v map; the margin
(10% by default) covers plane changes and corrections.

## Usage (step 3)

1. Set up the mission in the "Mission" tab (destination, type, margin).
2. Open the "Launchers" tab: the computation starts by itself and is redone when the mission or the payload changes.
3. Each card shows one variant, with stages listed from top to bottom: engines, tanks, decouplers,
   boosters, with the mass, cost, delta-v and TWR of each stage. The lightest (or cheapest)
   is marked "recommended".
4. Settings: optimize for mass or cost, minimum liftoff TWR (1.3 by default). With no vessel in
   the editor, enter the payload mass by hand.
5. "Choose this variant" selects the launcher, then "Build in the editor" assembles it.

## Usage (step 4)

1. In the "Launchers" tab, choose a variant then click "Build in the editor".
2. The launcher is attached under the lowest part of your payload (free bottom node) and the vessel is reloaded,
   staging sequence included: liftoff, booster drop, each stage, then the payload's stages.
   With no vessel in the editor, the launcher alone becomes the vessel.
3. To go back, load the "Before launcher" vessel (a copy saved just before).

Assembly details:
- switchable tanks (B9PartSwitch, CryoTanks) are set to the right contents;
- beyond one engine per stage, the first-stage engines are mounted under an unlocked multi-coupler
  (bi, tri or quad) sized to the tank; if no suitable coupler exists, they are attached in a cluster under the tank;
- each booster is capped with an unlocked nose cone of its size, unless its tank (or motor) already
  has a point; a nose tank is never stacked under another part;
- the decoupler under the payload takes the size of its bottom node;
- in asparagus staging, fuel lines (FTX-2, if unlocked) connect each booster pair to the next,
  then the last pair to the core; otherwise fuel transfers through the radial decouplers;
- each booster carries separation motors (Sepratron I, or the closest unlocked one), half at the top, half
  at the bottom, in a number suited to its dry mass; they ignite with its decoupler and push it away from the rocket;
- for a mission with a landing, if the payload's delta-v does not cover the descent (and what follows), a lander
  stage is added under it: vacuum engine, storable propellants, TWR ≥ 2 against the destination's
  gravity, four landing legs; the launcher then carries the payload and the lander;
- a copy of the last generated launcher is written to `GameData/LaunchPlanner/last-launcher.craft`: attach it
  with `KSP.log` if you report a problem.

Only unlocked parts are used. The computation assumes a stacked launcher; boosters are mounted
on radial decouplers.

Launcher shape:
- each stage takes the diameter that fills it with few tanks (a repeated main tank, at most
  one extra tank), without being narrower than the stage it carries: the base is wide and the launcher
  narrows toward the top;
- when the stage below is wider, an unlocked adapter tank caps the stage; otherwise a note
  asks you to add a structural adapter;
- the limits of the VAB (part count) and of the launch pad (height, width, mass) according to the building levels
  are respected, payload included; a launcher that is too tall is redone with wider tanks.
  In sandbox mode, there are no limits.

## What to check (step 1)

- The window opens and the "Engines" tab shows consistent values (for example the LV-T45 "Swivel":
  about 168 / 215 kN and 250 / 320 s).
- The "Source" line lists your part mods correctly.
- In career, only purchased parts are shown (the "Also show locked parts" checkbox shows the rest).

If you run into a problem, send the `KSP.log` file (game folder) and, if the window opens, the
`GameData/LaunchPlanner/inventory.csv` file created by the "Export CSV" button.

## Building

```
sudo apt install mono-mcs zip
KSPDIR=~/KSP ./build.sh       # folder containing KSP_x64_Data/Managed
```

You need at least `Assembly-CSharp.dll`, `UnityEngine.CoreModule.dll` and `UnityEngine.IMGUIModule.dll`.
Unity modules referenced by the game but unused by the mod are replaced by the empty stubs in `stubs/extra`.
B9PartSwitch is read through reflection: it is not needed to build.

Delta-v computation tests (compared with the community delta-v map):

```
mcs tests/MissionBudgetTest.cs src/MissionBudget.cs -out:build/test.exe && mono build/test.exe
```

Trying the launcher computation outside the game, from an `inventory.csv` exported by the mod
(arguments: payload mass in t, delta-v in m/s, and `stock` to restrict to stock parts):

```
mcs tests/LauncherDesignerTest.cs src/LauncherDesigner.cs src/PartInfo.cs src/CraftBuilder.cs src/CraftWriter.cs -out:build/designer.exe
mono build/designer.exe inventory.csv 5 4500
```

## License and notices

LaunchPlanner is released under the MIT License (see `LICENSE`). It contains no code or assets from the game or from
other mods: it only reads the parts loaded in your game at runtime. It is an unofficial fan-made mod, not affiliated
with or endorsed by Squad, Intercept Games or Take-Two Interactive. Kerbal Space Program is a trademark of its owners.
It collects no data and makes no network connection.
