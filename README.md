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
3. **Recommandation du lanceur (fait)** : onglet « Lanceurs ». Cinq variantes côte à côte (SSTO, deux étages,
   boosters à poudre, boosters liquides, asperges), chacune avec ses pièces exactes, sa masse, son coût,
   son Δv et son TWR par étage.
4. **Construction automatique dans le VAB (fait)** : le bouton « Construire dans l'éditeur » assemble la variante
   choisie (réservoirs, moteurs, découpleurs, boosters, étages) et la met en main, prête à être fixée sous la charge utile.

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

## Utilisation (étape 3)

1. Règle la mission dans l'onglet « Mission » (destination, type, marge).
2. Ouvre l'onglet « Lanceurs » : le calcul démarre tout seul et se refait quand la mission ou la charge utile change.
3. Chaque carte montre une variante, étages listés du haut vers le bas : moteurs, réservoirs, découpleurs,
   boosters, avec la masse, le coût, le Δv et le TWR de chaque étage. La plus légère (ou la moins chère)
   est marquée « recommandée ».
4. Réglages : optimiser la masse ou le coût, TWR minimal au décollage (1,3 par défaut). Sans vaisseau dans
   l'éditeur, on saisit la masse de la charge utile à la main.
5. « Choisir cette variante » retient le lanceur, puis « Construire dans l'éditeur » l'assemble.

## Utilisation (étape 4)

1. Dans l'onglet « Lanceurs », choisis une variante puis clique « Construire dans l'éditeur ».
2. Le lanceur apparaît accroché à la souris, comme un sous-assemblage. Approche son découpleur du haut du nœud
   du bas de ta charge utile et clique pour le fixer (sans vaisseau dans l'éditeur, clique dans le vide : il devient le vaisseau).
3. Vérifie l'ordre des étages dans la colonne de droite : décollage, largage des boosters, puis chaque étage.

Détails du montage :
- les réservoirs commutables (B9PartSwitch, CryoTanks) sont réglés sur le bon contenu ;
- au-delà d'un moteur par étage, les moteurs sont fixés en grappe sous le réservoir ;
- en asperges, le transfert de carburant est activé sur les découpleurs radiaux : le jeu vide d'abord les paires
  larguées en premier ;
- une copie du dernier lanceur généré est écrite dans `GameData/LaunchPlanner/dernier-lanceur.craft` : à joindre
  avec `KSP.log` en cas de problème.

Seules les pièces débloquées sont utilisées. Les adaptateurs ne sont pas proposés comme réservoirs : si un étage
change de diamètre, une note le signale. Le calcul suppose un lanceur empilé ; les boosters sont montés
sur des découpleurs radiaux.

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

Essai du calcul des lanceurs hors du jeu, à partir d'un `inventaire.csv` exporté par le mod
(arguments : masse de la charge utile en t, Δv en m/s, et `stock` pour se limiter aux pièces du jeu de base) :

```
mcs tests/LauncherDesignerTest.cs src/LauncherDesigner.cs src/PartInfo.cs src/CraftBuilder.cs src/CraftWriter.cs -out:build/designer.exe
mono build/designer.exe inventaire.csv 5 4500
```
