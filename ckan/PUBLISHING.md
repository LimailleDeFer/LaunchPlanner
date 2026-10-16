# Publishing LaunchPlanner on CKAN

CKAN does not store mods: it reads a metadata file (`LaunchPlanner.netkan`) from the community repository
[KSP-CKAN/NetKAN](https://github.com/KSP-CKAN/NetKAN), which points to the GitHub *releases* of this repository.
Each new release is then picked up automatically (within a few hours).

## One-time setup
1. Make this repository **public** (GitHub > Settings > General > Danger Zone > Change visibility).
   CKAN cannot download from a private repository.
2. Submit the metadata file to CKAN: open a *pull request* on KSP-CKAN/NetKAN that adds
   `NetKAN/LaunchPlanner.netkan` (a copy of `ckan/LaunchPlanner.netkan`). Their bot checks the file,
   and a volunteer approves it.

## For each version
1. Increase `VERSION` in `LaunchPlanner.version` (e.g. 0.1.0 → 0.2.0).
2. Build (`./build.sh`): the zip contains `GameData/LaunchPlanner/` with the DLL, the license and the `.version` file.
3. On GitHub: Releases > *Draft a new release*, tag `v0.2.0`, attach `LaunchPlanner.zip`, publish.

The `.version` file also tells CKAN which KSP versions are compatible (1.12.x).
