#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AirsoftFPS.Editor
{
    // Automated batch runs (play checks, screenshot tools started from the command line) play the game's
    // sound through the speakers of whoever is at the computer. In batch mode the editor's audio is muted
    // (the same switch as the Game view's "Mute Audio"); sources still play, so the audio checks still
    // see them. The previous setting is put back when the batch editor quits. The normal editor is untouched.
    [InitializeOnLoad]
    static class BatchAudioMute
    {
        const string Key = "Airsoft.BatchAudioMute.Prev";

        static BatchAudioMute()
        {
            if (!Application.isBatchMode) return;
            if (!SessionState.GetBool(Key + ".Set", false))
            {
                SessionState.SetBool(Key + ".Set", true);
                SessionState.SetBool(Key, EditorUtility.audioMasterMute);
            }
            EditorUtility.audioMasterMute = true;
            EditorApplication.quitting -= Restore;
            EditorApplication.quitting += Restore;
        }

        static void Restore() => EditorUtility.audioMasterMute = SessionState.GetBool(Key, false);
    }
}
#endif
