# LaunchPlanner : planificateur de lanceur pour KSP 1.12.5

Mod pour Kerbal Space Program 1.12.5 (avec ou sans Making History et Breaking Ground) qui propose la configuration
d'un lanceur (étages, moteurs, carburant, boosters) selon la charge utile et l'objectif de la mission, en n'utilisant
que les pièces débloquées, y compris celles des autres mods.

## Avancement

1. **Inventaire des pièces (fait)** : onglet « Pièces disponibles », qui liste les moteurs, boosters, réservoirs
   (y compris les réservoirs commutables B9PartSwitch / CryoTanks) et découpleurs chargés, avec poussée, Isp,
   masses et carburant.
2. **Budget Δv de la mission (fait)** : onglet « Mission ». On choisit la destination et le type de mission ;
   le vaisseau ouvert dans l'éditeur sert de charge utile, et son propre Δv est déduit de ce que le lanceur
   doit fournir.
3. Recommandation du lanceur, avec plusieurs variantes à comparer : SSTO, boosters à poudre, boosters
   liquides, asperges (boosters largués 2 par 2 qui alimentent le corps central).
4. Génération automatique du vaisseau dans le VAB.

## Installation

1. Télécharger `LaunchPlanner.zip`.
2. Le décompresser dans le dossier du jeu : on doit obtenir `Kerbal Space Program/GameData/LaunchPlanner/Plugins/LaunchPlanner.dll`.
3. Lancer KSP, ouvrir une partie et entrer dans le VAB ou le SPH.
4. Cliquer sur le bouton en forme de fusée dans la barre d'outils en bas à droite.

## Utilisation (étape 2)

1. Dans le VAB, charge le vaisseau qui sera la charge utile (par exemple l'atterrisseur et son module de retour).
2. Ouvre la fenêtre avec le bouton fusée, onglet « Mission ».
3. Choisis la destination dans la liste de gauche et le type de mission (orbite, atterrissage, avec ou sans retour).
4. Le tableau détaille chaque étape avec son Δv et indique si elle est assurée par la charge utile ou par le
   lanceur. En bas s'affiche ce que le lanceur doit fournir.

Le Δv propre de la charge utile vient du calcul du jeu (dans le vide). On peut aussi le saisir à la main.
Le budget est une estimation par transferts de Hohmann, proche de la carte Δv communautaire ; la marge
(10 % par défaut) couvre les changements de plan et les corrections.

## Ce qu'il faut vérifier (étape 1)

- La fenêtre s'ouvre et l'onglet « Moteurs » affiche des valeurs cohérentes (par exemple le LV-T45 « Swivel » :
  environ 168 / 215 kN et 250 / 320 s).
- La ligne « Origine » cite bien tes mods de pièces.
- En carrière, seules les pièces achetées s'affichent (la case « Afficher aussi les pièces non débloquées » montre le reste).

En cas de problème, envoyer le fichier `KSP.log` (dossier du jeu) et, si la fenêtre s'ouvre, le fichier
`GameData/LaunchPlanner/inventaire.csv` créé par le bouton « Exporter CSV ».

## Compilation

```
sudo apt install mono-mcs zip
KSPDIR=~/KSP ./build.sh       # dossier qui contient KSP_x64_Data/Managed
```

Il faut au minimum `Assembly-CSharp.dll`, `UnityEngine.CoreModule.dll` et `UnityEngine.IMGUIModule.dll`.
Les modules Unity cités par le jeu mais inutiles au mod sont remplacés par les stubs vides de `stubs/extra`.
B9PartSwitch est lu par réflexion : il n'est pas nécessaire pour compiler.

Tests du calcul de Δv (comparés à la carte Δv communautaire) :

```
mcs tests/MissionBudgetTest.cs src/MissionBudget.cs -out:build/test.exe && mono build/test.exe
```
