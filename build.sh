#!/usr/bin/env bash
# Compile le mod avec le compilateur C# de Mono (paquet mono-mcs), contre les DLL du jeu :
#
#   KSPDIR=~/KSP ./build.sh        (dossier qui contient KSP_x64_Data/Managed)
#
# Résultat : build/GameData/LaunchPlanner (à copier dans le GameData de KSP) et build/LaunchPlanner.zip
set -euo pipefail
cd "$(dirname "$0")"

OUT=build
MOD="$OUT/GameData/LaunchPlanner"
rm -rf "$OUT"
mkdir -p "$OUT/ref" "$MOD/Plugins"
EXTRA=()

if [ -z "${KSPDIR:-}" ]; then
  echo "Indique le dossier de KSP : KSPDIR=/chemin/vers/KSP ./build.sh" >&2
  exit 1
fi
MANAGED="$KSPDIR/KSP_x64_Data/Managed"
[ -d "$MANAGED" ] || MANAGED="$KSPDIR/KSP_Data/Managed"
[ -d "$MANAGED" ] || MANAGED="$KSPDIR/KSP.app/Contents/Resources/Data/Managed"
REF="$MANAGED"
echo "Compilation contre les DLL de KSP : $REF"
# Assembly-CSharp cite des modules Unity que le mod n'utilise pas : s'ils ne sont pas
# fournis, des stubs vides (stubs/extra) suffisent pour compiler.
for stub in stubs/extra/*.cs; do
  name=$(basename "$stub" .cs)
  if [ -f "$REF/$name.dll" ]; then EXTRA+=(-r:"$REF/$name.dll"); continue; fi
  mcs -nologo -target:library -out:"$OUT/ref/$name.dll" -r:"$REF/UnityEngine.CoreModule.dll" "$stub"
  EXTRA+=(-r:"$OUT/ref/$name.dll")
done

echo "Compilation du mod"
mcs -nologo -target:library -optimize -sdk:4.5 -out:"$MOD/Plugins/LaunchPlanner.dll" \
  -r:"$REF/Assembly-CSharp.dll" -r:"$REF/UnityEngine.CoreModule.dll" -r:"$REF/UnityEngine.IMGUIModule.dll" "${EXTRA[@]}" \
  src/*.cs

cp README.md "$MOD/LISEZMOI.md"
(cd "$OUT" && rm -f LaunchPlanner.zip && zip -qr LaunchPlanner.zip GameData)
echo "OK : $MOD/Plugins/LaunchPlanner.dll et $OUT/LaunchPlanner.zip"
