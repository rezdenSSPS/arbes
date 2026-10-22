#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AirsoftFPS.Editor
{
    // Import settings for the Quaternius CC0 low-poly models.
    //  - characters: Legacy animation (so the runtime can play Idle/Walk/Run/Death by name)
    //  - guns: readable meshes (the runtime measures them to orient/scale the model)
    //  - map props: static, no animation, readable meshes (EnemySpawner rebuilds the NavMesh at
    //    runtime, which needs read access in a player build)
    public class AirsoftModelPostprocessor : AssetPostprocessor
    {
        const string Root = "Assets/ThirdParty/Quaternius/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root)) return;
            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.addCollider = false;

            if (assetPath.Contains("/AirsoftCharacters/"))
            {
                mi.animationType = ModelImporterAnimationType.Legacy;
                mi.importAnimation = true;
                mi.isReadable = false;
            }
            else if (assetPath.Contains("/AirsoftGuns/"))
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                mi.isReadable = true;
            }
            else
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                mi.isReadable = true;
            }
        }
    }

    [InitializeOnLoad]
    static class AirsoftAssetSetup
    {
        const string DoneKey = "Airsoft.QuaterniusImport.v2";

        static AirsoftAssetSetup() { EditorApplication.delayCall += RunOnce; }

        static void RunOnce()
        {
            if (EditorPrefs.GetBool(DoneKey, false)) return;
            EditorPrefs.SetBool(DoneKey, true);
            Reimport();
        }

        [MenuItem("Tools/Airsoft/Reimport Quaternius Assets")]
        public static void Reimport()
        {
            if (!AssetDatabase.IsValidFolder("Assets/ThirdParty/Quaternius")) return;
            AssetDatabase.ImportAsset("Assets/ThirdParty/Quaternius",
                ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("[Airsoft] Quaternius models re-imported with Airsoft settings.");
        }
    }
}
#endif
