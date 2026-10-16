#!/usr/bin/env bash
# Builds the mod with the Mono C# compiler (mono-mcs package), against the game DLLs:
#
#   KSPDIR=~/KSP ./build.sh        (folder containing KSP_x64_Data/Managed)
#
# Output: build/GameData/LaunchPlanner (copy it into KSP's GameData) and build/LaunchPlanner.zip
set -euo pipefail
cd "$(dirname "$0")"

OUT=build
MOD="$OUT/GameData/LaunchPlanner"
rm -rf "$OUT"
mkdir -p "$OUT/ref" "$MOD/Plugins"
EXTRA=()

if [ -z "${KSPDIR:-}" ]; then
  echo "Set the KSP folder: KSPDIR=/path/to/KSP ./build.sh" >&2
  exit 1
fi
MANAGED="$KSPDIR/KSP_x64_Data/Managed"
[ -d "$MANAGED" ] || MANAGED="$KSPDIR/KSP_Data/Managed"
[ -d "$MANAGED" ] || MANAGED="$KSPDIR/KSP.app/Contents/Resources/Data/Managed"
REF="$MANAGED"
echo "Building against the KSP DLLs: $REF"
# Assembly-CSharp references Unity modules the mod does not use: if they are not
# provided, empty stubs (stubs/extra) are enough to compile.
for stub in stubs/extra/*.cs; do
  name=$(basename "$stub" .cs)
  if [ -f "$REF/$name.dll" ]; then EXTRA+=(-r:"$REF/$name.dll"); continue; fi
  mcs -nologo -target:library -out:"$OUT/ref/$name.dll" -r:"$REF/UnityEngine.CoreModule.dll" "$stub"
  EXTRA+=(-r:"$OUT/ref/$name.dll")
done

echo "Building the mod"
mcs -nologo -target:library -optimize -sdk:4.5 -out:"$MOD/Plugins/LaunchPlanner.dll" \
  -r:"$REF/Assembly-CSharp.dll" -r:"$REF/UnityEngine.CoreModule.dll" -r:"$REF/UnityEngine.IMGUIModule.dll" "${EXTRA[@]}" \
  src/*.cs

cp README.md "$MOD/README.md"
cp LaunchPlanner.version LICENSE "$MOD/"
(cd "$OUT" && rm -f LaunchPlanner.zip && zip -qr LaunchPlanner.zip GameData)
echo "OK: $MOD/Plugins/LaunchPlanner.dll and $OUT/LaunchPlanner.zip"
