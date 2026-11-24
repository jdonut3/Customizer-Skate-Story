// Writes all the text of the game to files, to read it outside the game:
//  ShoeMod/game_text.txt: every text entry of the game's text tables (the I2 localization terms), all languages.
//  ShoeMod/level_text_<level>.txt: every text object in the loaded level, also the turned-off and hidden ones
//  (TextMeshPro texts, UI texts and 3D text meshes), with the object's place in the level.
// Reflection only: the text library of the game is not needed at build time.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

public static class TextDump
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static string Dir { get { return Path.Combine(Application.persistentDataPath, "ShoeMod"); } }

    public static string Run()
    {
        int terms = 0, objs = 0;
        try { terms = Terms(); } catch (Exception e) { File.WriteAllText(Path.Combine(Dir, "game_text_error.txt"), e.ToString()); }
        try { objs = LevelText(); } catch (Exception e) { File.WriteAllText(Path.Combine(Dir, "level_text_error.txt"), e.ToString()); }
        return "Wrote " + terms + " text entries and " + objs + " text objects of this level to the ShoeMod folder.";
    }

    static object Get(object o, string name)
    {
        if (o == null) return null;
        Type t = o.GetType();
        FieldInfo f = t.GetField(name, All); if (f != null) return f.GetValue(o);
        PropertyInfo p = t.GetProperty(name, All); if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(o, null);
        return null;
    }

    static int Terms()
    {
        StringBuilder sb = new StringBuilder();
        HashSet<object> seen = new HashSet<object>();
        List<object> termLists = new List<object>();
        // the source: LocMan.GetI2LanguageSource(), its term list (mTerms) and its language list (mLanguages)
        object src = null;
        MethodInfo gs = typeof(LocMan).GetMethod("GetI2LanguageSource", All);
        if (gs != null) { try { src = gs.Invoke(null, null); } catch (Exception) { } }
        List<string> langs = new List<string>();
        if (src != null)
        {
            IEnumerable ls = Get(src, "mLanguages") as IEnumerable;
            if (ls != null) foreach (object l in ls) { object n = Get(l, "Name"); langs.Add(n != null ? n.ToString() : "?"); }
            object mt = Get(src, "mTerms"); if (mt != null) termLists.Add(mt);
        }
        // also the English table of LocMan
        FieldInfo ed = typeof(LocMan).GetField("english_dict", All);
        if (ed != null) { IDictionary d = ed.GetValue(null) as IDictionary; if (d != null) termLists.Add(d.Values); }
        sb.Append("LANGUAGES: ").Append(string.Join(", ", langs.ToArray())).Append("\n\n");
        int n0 = 0;
        foreach (object list in termLists)
        {
            IEnumerable e = list as IEnumerable; if (e == null) continue;
            foreach (object td in e)
            {
                if (td == null || !seen.Add(td)) continue;
                object key = Get(td, "Term");
                sb.Append("### ").Append(key).Append('\n');
                string[] vals = Get(td, "Languages") as string[];
                if (vals != null)
                    for (int i = 0; i < vals.Length; i++)
                        if (!string.IsNullOrEmpty(vals[i])) sb.Append("  [").Append(i < langs.Count ? langs[i] : i.ToString()).Append("] ").Append(vals[i].Replace("\n", "\\n")).Append('\n');
                n0++;
            }
        }
        File.WriteAllText(Path.Combine(Dir, "game_text.txt"), sb.ToString());
        return n0;
    }

    static string PathOf(Transform t)
    {
        string s = t.name; int d = 0;
        while (t.parent != null && d < 10) { t = t.parent; s = t.name + "/" + s; d++; }
        return s;
    }
    static int LevelText()
    {
        StringBuilder sb = new StringBuilder();
        int n = 0;
        List<Type> types = new List<Type>();
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = null;
            try { t = a.GetType("TMPro.TMP_Text"); } catch (Exception) { }
            if (t != null) types.Add(t);
        }
        types.Add(typeof(UnityEngine.UI.Text));
        types.Add(typeof(TextMesh));
        HashSet<int> done = new HashSet<int>();
        foreach (Type ty in types)
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(ty))
            {
                Component c = o as Component; if (c == null || !done.Add(c.GetInstanceID())) continue;
                if (!c.gameObject.scene.IsValid()) continue;          // only objects in the loaded scenes (not prefabs on disk)
                object txt = Get(c, "text"); string s = txt as string;
                if (string.IsNullOrEmpty(s) || s.Trim().Length == 0) continue;
                sb.Append("### ").Append(c.gameObject.scene.name).Append(" | ").Append(PathOf(c.transform)).Append(c.gameObject.activeInHierarchy ? "" : "  (turned off)")
                  .Append("  at ").Append(c.transform.position.ToString()).Append('\n').Append(s.Replace("\n", "\\n")).Append("\n\n");
                n++;
            }
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        File.WriteAllText(Path.Combine(Dir, "level_text_" + scene + ".txt"), sb.ToString());
        return n;
    }
}
