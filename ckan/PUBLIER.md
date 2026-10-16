# Publier LaunchPlanner sur CKAN

CKAN ne stocke pas les mods : il lit une fiche (`LaunchPlanner.netkan`) dans le dépôt communautaire
[KSP-CKAN/NetKAN](https://github.com/KSP-CKAN/NetKAN), qui pointe vers les *releases* GitHub de ce dépôt.
Chaque nouvelle release est ensuite détectée toute seule (en quelques heures).

## Une seule fois
1. Rendre ce dépôt **public** (GitHub > Settings > General > Danger Zone > Change visibility).
   CKAN ne peut pas télécharger depuis un dépôt privé.
2. Proposer la fiche à CKAN : ouvrir une *pull request* sur KSP-CKAN/NetKAN qui ajoute
   `NetKAN/LaunchPlanner.netkan` (copie de `ckan/LaunchPlanner.netkan`). Leur robot vérifie la fiche,
   un bénévole la valide.

## À chaque version
1. Augmenter `VERSION` dans `LaunchPlanner.version` (ex. 0.1.0 → 0.2.0).
2. Compiler (`./build.sh`) : le zip contient `GameData/LaunchPlanner/` avec la DLL, la licence et le `.version`.
3. Sur GitHub : Releases > *Draft a new release*, tag `v0.2.0`, joindre `LaunchPlanner.zip`, publier.

Le fichier `.version` indique aussi à CKAN les versions de KSP compatibles (1.12.x).
