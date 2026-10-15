#!/usr/bin/env bash
# Compile le mod avec le compilateur C# de Mono (paquet mono-mcs).
#
#   ./build.sh                     compile contre les stubs de stubs/ (aucune installation de KSP requise)
#   KSPDIR=~/KSP ./build.sh        compile contre les vraies DLL de KSP (KSP_x64_Data/Managed)
#
# Résultat : build/GameData/LaunchPlanner (à copier dans le GameData de KSP) et build/LaunchPlanner.zip
set -euo pipefail
cd "$(dirname "$0")"

OUT=build
REF="$OUT/ref"
MOD="$OUT/GameData/LaunchPlanner"
rm -rf "$OUT"
mkdir -p "$REF" "$MOD/Plugins"
EXTRA=()

if [ -n "${KSPDIR:-}" ]; then
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
else
  echo "Compilation des stubs de référence"
  mcs -nologo -target:library -out:"$REF/UnityEngine.CoreModule.dll" stubs/UnityEngine.CoreModule.cs
  mcs -nologo -target:library -out:"$REF/UnityEngine.IMGUIModule.dll" -r:"$REF/UnityEngine.CoreModule.dll" stubs/UnityEngine.IMGUIModule.cs
  mcs -nologo -target:library -out:"$REF/Assembly-CSharp.dll" -r:"$REF/UnityEngine.CoreModule.dll" stubs/Assembly-CSharp.cs
fi

echo "Compilation du mod"
mcs -nologo -target:library -optimize -sdk:4.5 -out:"$MOD/Plugins/LaunchPlanner.dll" \
  -r:"$REF/Assembly-CSharp.dll" -r:"$REF/UnityEngine.CoreModule.dll" -r:"$REF/UnityEngine.IMGUIModule.dll" "${EXTRA[@]}" \
  src/*.cs

cp README.md "$MOD/LISEZMOI.md"
(cd "$OUT" && rm -f LaunchPlanner.zip && zip -qr LaunchPlanner.zip GameData)
echo "OK : $MOD/Plugins/LaunchPlanner.dll et $OUT/LaunchPlanner.zip"
