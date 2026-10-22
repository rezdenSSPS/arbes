using UnityEngine;

namespace AirsoftFPS
{
    // Marks geometry (roofs, ceilings) that the top-down minimap shot and the editor map previews
    // leave out, so the rooms underneath show. Its renderers are only hidden for that one render.
    public class MinimapHidden : MonoBehaviour
    {
    }
}
