using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirsoftFPS
{
    // Recolours the interface that is already built when the accent theme changes (UIKit.SetTheme).
    // The screens are built in code and keep the colours they were made with, so this walks everything
    // loaded and swaps the old accent shades for the new ones, keeping each one's alpha:
    //  - the colour of every UI graphic (images, text, the minimap arrow…);
    //  - accent hex codes in rich text (<color=#FF7A1A>…);
    //  - Color fields of this game's components (button states, segment looks…), so hover / select
    //    states do not bring the old colour back.
    // Colours computed every frame from UIKit.Accent pick up the new theme by themselves.
    // Marks a part of the UI that shows fixed colours (colour swatches, the crosshair): never recoloured.
    public class ThemeFixed : MonoBehaviour { }

    static class ThemeRecolour
    {
        static readonly Dictionary<Type, FieldInfo[]> colourFields = new Dictionary<Type, FieldInfo[]>();

        public static void Run(Color[] from, Color[] to)
        {
            if (!Application.isPlaying) return;
            var patterns = new Regex[from.Length];
            for (int i = 0; i < from.Length; i++)
                patterns[i] = new Regex(Regex.Escape(UIKit.Html(from[i])), RegexOptions.IgnoreCase);

            foreach (var g in Resources.FindObjectsOfTypeAll<Graphic>())
            {
                if (!g.gameObject.scene.IsValid()) continue;   // prefabs / assets
                if (g.GetComponentInParent<ThemeFixed>(true) != null) continue;
                g.color = Swap(g.color, from, to);
                if (g is TMP_Text t && !string.IsNullOrEmpty(t.text) && t.text.IndexOf('#') >= 0)
                {
                    string s = t.text;
                    for (int i = 0; i < from.Length; i++) s = patterns[i].Replace(s, UIKit.Html(to[i]));
                    if (s != t.text) t.text = s;
                }
            }
            foreach (var input in Resources.FindObjectsOfTypeAll<TMP_InputField>())
            {
                if (!input.gameObject.scene.IsValid()) continue;
                input.caretColor = Swap(input.caretColor, from, to);
                input.selectionColor = Swap(input.selectionColor, from, to);
            }
            foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (mb == null || !mb.gameObject.scene.IsValid()) continue;
                var type = mb.GetType();
                if (type.Namespace != "AirsoftFPS" || mb.GetComponentInParent<ThemeFixed>(true) != null) continue;
                foreach (var f in FieldsOf(type))
                {
                    var c = (Color)f.GetValue(mb);
                    var n = Swap(c, from, to);
                    if (n != c) f.SetValue(mb, n);
                }
                if (mb is KitButton kb) kb.Refresh();
            }
        }

        static FieldInfo[] FieldsOf(Type type)
        {
            if (colourFields.TryGetValue(type, out var list)) return list;
            var found = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (f.FieldType == typeof(Color) && !f.IsInitOnly) found.Add(f);
            return colourFields[type] = found.ToArray();
        }

        static Color Swap(Color c, Color[] from, Color[] to)
        {
            for (int i = 0; i < from.Length; i++)
                if (Same(c, from[i])) { var n = to[i]; n.a = c.a; return n; }
            return c;
        }

        static bool Same(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.006f && Mathf.Abs(a.g - b.g) < 0.006f && Mathf.Abs(a.b - b.b) < 0.006f;
    }
}
