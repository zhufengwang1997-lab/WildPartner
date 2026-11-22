#!/usr/bin/env bash
# Batch-process the Tripo GLBs in _Inbox (tools/blender/process_tripo.py), 4 Blender processes at a time.
#   tools/tripo/process_all.sh [Name ...]     (no names = all in the table)
cd "$(dirname "$0")/../.."
B=/Applications/Blender.app/Contents/MacOS/Blender
TABLE='Tree_Broadleaf Tree_Broadleaf h=9 6000,1800,400
Tree_Birch Tree_Birch h=10 6000,1800,400
Tree_Autumn Tree_Autumn h=8.5 6000,1800,400
Tree_Pine Tree_Pine h=12 6000,1800,400
Bush Bush h=1.6 2500,800,200
Rock_Large Rock_Large h=2.8 3000,900,200
Rock_Small Rock_Small h=0.7 1200,400
Bridge Bridge l=16 12000,3000
Cabin Cabin h=7.5 30000,8000,2000
Fence Fence l=3 2000,600
LampPost LampPost h=2.8 2500,800
FlagPole FlagPole h=3.4 2500,800
Watchtower Watchtower h=12 15000,4000,1000
StandingStone StandingStone h=3.2 2500,800,200
Stump Stump h=0.9 2500,700
Signpost Signpost h=1.9 2000,600
Cauldron Cauldron h=0.9 4000,1200
Planter Planter h=0.8 4000,1200
Chest Chest h=0.75 3000,900
Woodpile Woodpile h=1.1 3000,900
Book Book l=0.32 1500
mushroom Mushroom h=0.35 1500,400
MoonFlower MoonFlower h=1.7 8000,2500'
mkdir -p Logs/tripo
echo "$TABLE" | while read -r src name size lods; do
  if [ $# -gt 0 ] && ! printf '%s\n' "$@" | grep -qx "$name"; then continue; fi
  echo "$src $name $size $lods"
done | xargs -P 4 -L 1 sh -c '"'$B'" -b -P tools/blender/process_tripo.py -- "Assets/_Game/Models/_Inbox/$0.glb" Assets/_Game/Models/Tripo "$1" "$2" "$3" "Logs/tripo/$1.png" 2>&1 | grep -E "^RESULT|Error|Traceback" | sed "s/^/[$1] /"'
