# LaunchPlanner : planificateur de lanceur pour KSP 1.12.5

Mod pour Kerbal Space Program 1.12.5 (avec ou sans Making History et Breaking Ground) qui propose la configuration
d'un lanceur (étages, moteurs, carburant, boosters) selon la charge utile et l'objectif de la mission, en n'utilisant
que les pièces débloquées, y compris celles des autres mods.

## Avancement

1. **Inventaire des pièces (fait)** : une fenêtre dans le VAB/SPH liste les moteurs, boosters, réservoirs et
   découpleurs chargés (jeu de base, DLC et mods), avec poussée, Isp, masses et carburant.
2. Budget Δv de la mission : objectif, lecture de la charge utile et de son propre Δv.
3. Recommandation du lanceur, étage par étage.
4. Génération automatique du vaisseau dans le VAB.

## Installation

1. Télécharger `LaunchPlanner.zip`.
2. Le décompresser dans le dossier du jeu : on doit obtenir `Kerbal Space Program/GameData/LaunchPlanner/Plugins/LaunchPlanner.dll`.
3. Lancer KSP, ouvrir une partie et entrer dans le VAB ou le SPH.
4. Cliquer sur le bouton en forme de fusée dans la barre d'outils en bas à droite.

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
./build.sh                    # compile contre les stubs de stubs/, sans installation de KSP
KSPDIR=~/KSP ./build.sh       # ou contre les vraies DLL du jeu
```

Les fichiers de `stubs/` ne reproduisent que la petite partie de l'API de KSP et d'Unity utilisée par le mod.
Ils servent uniquement à compiler ; en jeu, ce sont les vraies DLL de KSP qui sont utilisées.
