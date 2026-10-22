#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace AirsoftFPS.Editor
{
    // Audio assets setup:
    //  * Tools/Airsoft/Audio/Create Mixer builds Resources/AirsoftAudio/AirsoftMixer.mixer: Master with the
    //    child groups SFX, Music and UI, and the group volumes exposed as MasterVolume / SfxVolume /
    //    MusicVolume / UiVolume (AirsoftAudio.ApplyVolumes sets them from Settings > Audio).
    //    Unity has no public API to author mixers, so this goes through the editor's own
    //    AudioMixerController (the class behind Create > Audio Mixer) by reflection.
    //  * AirsoftAudioImport (below) gives the generated clips sensible import settings.
    //
    // Batch: Unity -batchmode -projectPath <p> -executeMethod AirsoftFPS.Editor.AirsoftAudioSetup.RunBatch
    public static class AirsoftAudioSetup
    {
        public const string MixerPath = "Assets/Resources/AirsoftAudio/AirsoftMixer.mixer";

        static readonly (string group, string param)[] Groups =
        {
            ("SFX", AirsoftAudio.P_SFX), ("Music", AirsoftAudio.P_MUSIC), ("UI", AirsoftAudio.P_UI),
        };

        const BindingFlags ANY = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [MenuItem("Tools/Airsoft/Audio/Create Mixer")]
        static void CreateMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath) != null &&
                !EditorUtility.DisplayDialog("Audio mixer", "Replace the existing " + MixerPath + "?", "Replace", "Cancel"))
                return;
            CreateMixer();
            Debug.Log(Verify(out string report) ? "[Airsoft] mixer OK\n" + report : "[Airsoft] mixer problems\n" + report);
        }

        public static void RunBatch()
        {
            int code = 0;
            try
            {
                AssetDatabase.Refresh();
                if (AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath) == null || !Verify(out _)) CreateMixer();
                bool ok = Verify(out string report);
                Debug.Log("[Airsoft] audio setup " + (ok ? "OK" : "FAILED") + "\n" + report);
                if (!ok) code = 1;
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        public static void CreateMixer()
        {
            var asm = typeof(UnityEditor.Editor).Assembly;
            var tCtrl = asm.GetType("UnityEditor.Audio.AudioMixerController", true);
            var tGroup = asm.GetType("UnityEditor.Audio.AudioMixerGroupController", true);
            var tPath = asm.GetType("UnityEditor.Audio.AudioGroupParameterPath", true);
            var tExposed = asm.GetType("UnityEditor.Audio.ExposedAudioParameter", true);

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(MixerPath));
            if (AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath) != null) AssetDatabase.DeleteAsset(MixerPath);

            var ctrl = Call(tCtrl, null, "CreateMixerControllerAtPath", MixerPath);
            var master = Get(tCtrl, ctrl, "masterGroup");

            var exposeFor = new List<(object group, string name)> { (master, AirsoftAudio.P_MASTER) };
            foreach (var (name, param) in Groups)
            {
                var g = Call(tCtrl, ctrl, "CreateNewGroup", name, false);
                Call(tCtrl, ctrl, "AddChildToParent", g, master);
                exposeFor.Add((g, param));
            }
            SetDefaultView(asm, tCtrl, tGroup, ctrl, exposeFor);

            // expose each group's volume, then give the parameters our names
            var names = new Dictionary<GUID, string>();
            foreach (var (g, name) in exposeFor)
            {
                var guid = (GUID)Call(tGroup, g, "GetGUIDForVolume");
                var path = Activator.CreateInstance(tPath, ANY, null, new[] { g, (object)guid }, null);
                Call(tCtrl, ctrl, "AddExposedParameter", path);
                names[guid] = name;
            }
            var prop = tCtrl.GetProperty("exposedParameters", ANY);
            var arr = (Array)prop.GetValue(ctrl);
            var fGuid = tExposed.GetField("guid", ANY);
            var fName = tExposed.GetField("name", ANY);
            for (int i = 0; i < arr.Length; i++)
            {
                object e = arr.GetValue(i);
                if (names.TryGetValue((GUID)fGuid.GetValue(e), out string n)) { fName.SetValue(e, n); arr.SetValue(e, i); }
            }
            prop.SetValue(ctrl, arr);
            TryCall(tCtrl, ctrl, "OnChangedExposedParameter");

            EditorUtility.SetDirty((UnityEngine.Object)ctrl);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MixerPath, ImportAssetOptions.ForceUpdate);
        }

        // One "View" listing every group, like a mixer made from the Create menu (the Audio Mixer window
        // shows the groups of the current view). Cosmetic: the mixer works without it.
        static void SetDefaultView(Assembly asm, Type tCtrl, Type tGroup, object ctrl, List<(object group, string name)> groups)
        {
            try
            {
                var tView = asm.GetType("UnityEditor.Audio.MixerGroupView", true);
                var guids = new List<GUID>();
                foreach (var (g, _) in groups) guids.Add((GUID)Get(tGroup, g, "groupID"));
                object view = Activator.CreateInstance(tView);
                tView.GetField("guids", ANY).SetValue(view, guids.ToArray());
                tView.GetField("name", ANY).SetValue(view, "View");
                var arr = Array.CreateInstance(tView, 1);
                arr.SetValue(view, 0);
                tCtrl.GetProperty("views", ANY).SetValue(ctrl, arr);
            }
            catch (Exception e) { Debug.Log("[Airsoft] mixer setup: no group view (" + e.GetType().Name + ": " + e.Message + ")"); }
        }

        // The mixer exists, has the groups and every exposed parameter answers.
        public static bool Verify(out string report)
        {
            var sb = new System.Text.StringBuilder();
            bool ok = true;
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null) { report = "no mixer at " + MixerPath; return false; }
            foreach (var (name, _) in Groups)
            {
                var found = mixer.FindMatchingGroups(name);
                bool has = Array.Exists(found, g => g.name == name);
                ok &= has;
                sb.AppendLine((has ? "  ok   " : "  MISS ") + "group " + name);
            }
            foreach (var p in new[] { AirsoftAudio.P_MASTER, AirsoftAudio.P_SFX, AirsoftAudio.P_MUSIC, AirsoftAudio.P_UI })
            {
                bool has = mixer.GetFloat(p, out float v);
                ok &= has;
                sb.AppendLine((has ? "  ok   " : "  MISS ") + "exposed " + p + (has ? " = " + v + " dB" : ""));
            }
            report = sb.ToString();
            return ok;
        }

        // ───────── reflection helpers ─────────
        static object Call(Type t, object target, string method, params object[] args)
        {
            var m = Find(t, method, args.Length);
            if (m == null) throw new MissingMethodException(t.FullName + "." + method + " (" + args.Length + " args); has: " + Members(t, method));
            return m.Invoke(target, args);
        }

        // Optional steps (e.g. the mixer window's view list, which a mixer made outside the window doesn't have yet).
        static void TryCall(Type t, object target, string method, params object[] args)
        {
            var m = Find(t, method, args.Length);
            try { if (m != null) m.Invoke(target, args); }
            catch (TargetInvocationException e) { Debug.Log("[Airsoft] mixer setup: skipped " + method + " (" + e.InnerException?.GetType().Name + ")"); }
        }

        static MethodInfo Find(Type t, string method, int argc)
        {
            foreach (var m in t.GetMethods(ANY))
                if (m.Name == method && m.GetParameters().Length == argc) return m;
            return null;
        }

        static object Get(Type t, object target, string prop)
        {
            var p = t.GetProperty(prop, ANY);
            if (p != null) return p.GetValue(target);
            var f = t.GetField(prop, ANY);
            if (f != null) return f.GetValue(target);
            throw new MissingMemberException(t.FullName, prop);
        }

        static string Members(Type t, string like)
        {
            var list = new List<string>();
            foreach (var m in t.GetMethods(ANY))
                if (m.Name.IndexOf(like, StringComparison.OrdinalIgnoreCase) >= 0 || like.IndexOf(m.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                    list.Add(m.ToString());
            return string.Join("; ", list);
        }
    }

    // Import settings for Resources/AirsoftAudio: one-shots decompressed on load (no decode cost when
    // they play, Vorbis on disk), music / ambience streamed.
    class AirsoftAudioImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/Resources/AirsoftAudio/", StringComparison.Ordinal)) return;
            var imp = (AudioImporter)assetImporter;
            bool music = assetPath.Contains("/Music/");
            var s = imp.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            if (music)
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.quality = 0.6f;
                imp.forceToMono = false;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.quality = 0.75f;
                imp.forceToMono = true;
            }
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            imp.defaultSampleSettings = s;
            imp.loadInBackground = music;
        }
    }
}
#endif
