#!/bin/zsh
# Re-shoots the main-menu background pictures (Assets/Resources/MenuShots) from real matches with bots.
# One Unity batch run per map / mode / replica; see Assets/Editor/MenuShotsCapture.cs.
# Usage: tools/menu_shots.sh   (the project must not be open in the Unity editor)
U=${UNITY:-/Applications/Unity/Hub/Editor/6000.6.8f1/Unity.app/Contents/MacOS/Unity}
cd "$(dirname "$0")/.."
# map  mode  weapon  delay      (modes: 1 FFA, 2 TDM, 5 CTF, 6 DOM, 8 Bomb, 9 VIP; maps: 0 Town, 1 Warehouse, 2 Outpost, 3 Forest)
combos=(
  "0 2 5 0"
  "1 1 3 0"
  "2 6 6 0"
  "3 8 8 8"
  "0 5 2 0"
  "3 2 9 0"
  "2 9 0 8"
  "1 6 4 0"
)
for c in $combos; do
  set -- ${=c}
  echo "map $1 mode $2 weapon $3"
  "$U" -batchmode -projectPath "$PWD" -executeMethod AirsoftFPS.Editor.MenuShotsCapture.RunBatch \
    -shotMode $2 -shotMap $1 -shotWeapon $3 -shotDelay $4 -shotSeed $RANDOM -logFile "$PWD/Logs/menushots_m$1_g$2.log"
  grep -E "Menu shot" "$PWD/Logs/menushots_m$1_g$2.log"
done
