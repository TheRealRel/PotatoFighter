#!/bin/bash
# Paths: set VALHEIM_MANAGED to Valheim/valheim_Data/Managed and BEPINEX to a profile's BepInEx folder (with Jotunn installed).
M="${VALHEIM_MANAGED:-/mnt/user-data/uploads/Managed}"; B="${BEPINEX:-/mnt/user-data/uploads/Potato Server-Mods/BepInEx}"
J="${JOTUNN:-$B/plugins/ValheimModding-Jotunn/Jotunn.dll}"
CSC="${CSC:-dotnet /usr/lib/dotnet/sdk/8.0.131/Roslyn/bincore/csc.dll}"
python3 -I check_config_names.py src/*.cs || { echo "Build stopped: fix the setting names above."; exit 1; }
python3 -I check_patches.py src/*.cs || { echo "Build stopped: a patch class above is never switched on."; exit 1; }
mkdir -p out
$CSC -nologo -nostdlib -noconfig -target:library -optimize+ -deterministic -langversion:9 -out:out/PotatoFighter.dll -r:"$M/mscorlib.dll" -r:"$M/netstandard.dll" -r:"$M/System.dll" -r:"$M/System.Core.dll" -r:"$M/assembly_valheim.dll" -r:"$M/assembly_utils.dll" -r:"$M/assembly_guiutils.dll" -r:"$M/UnityEngine.dll" -r:"$M/UnityEngine.CoreModule.dll" -r:"$M/UnityEngine.PhysicsModule.dll" -r:"$M/UnityEngine.InputLegacyModule.dll" -r:"$M/Unity.TextMeshPro.dll" -r:"$M/UnityEngine.UI.dll" -r:"$M/Splatform.dll" -r:"$M/UnityEngine.ImageConversionModule.dll" -r:"$M/UnityEngine.ParticleSystemModule.dll" -r:"$M/UnityEngine.AudioModule.dll" -r:"$M/UnityEngine.AIModule.dll" -r:"$M/UnityEngine.AssetBundleModule.dll" -r:"$B/core/BepInEx.dll" -r:"$B/core/0Harmony.dll" -r:"$J" src/*.cs
