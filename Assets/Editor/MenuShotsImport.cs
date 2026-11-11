#if UNITY_EDITOR
using UnityEditor;

namespace AirsoftFPS.Editor
{
    // Menu background pictures (Resources/MenuShots): keep them 16:9 (no power-of-two rescale), no mipmaps
    // (always shown near full size), good-quality compression.
    public class MenuShotsImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/MenuShots/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.mipmapEnabled = false;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        }
    }
}
#endif
