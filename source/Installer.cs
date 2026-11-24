// Installer for the Skate Story "In-Game Editor" mod (ShoeMod).
// It changes a few game files on this computer with patch files, and it can put the original files back.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

// Console output that also goes into a log file next to the installer.
class Tee : TextWriter
{
    TextWriter con; public StringBuilder log = new StringBuilder();
    public Tee(TextWriter c) { con = c; }
    public override Encoding Encoding { get { return con.Encoding; } }
    public override void Write(char c) { con.Write(c); log.Append(c); }
    public override void Write(string s) { con.Write(s); log.Append(s); }
    public override void WriteLine(string s) { con.WriteLine(s); log.Append(s).Append("\r\n"); }
    public override void WriteLine() { con.WriteLine(); log.Append("\r\n"); }
}

class Patch
{
    public string file, rel; public long origLen, newLen; public byte[] origSha, newSha, ops;
    public static Patch Read(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        if (b.Length < 90 || b[0] != 'S' || b[1] != 'M' || b[2] != 'P' || b[3] != '1') throw new Exception("Bad patch file: " + path);
        Patch p = new Patch(); p.file = path;
        int n = BitConverter.ToInt32(b, 4); int q = 8 + n;
        p.rel = Encoding.UTF8.GetString(b, 8, n).Replace('/', Path.DirectorySeparatorChar);
        p.origLen = BitConverter.ToInt64(b, q); p.origSha = Sub(b, q + 8, 32);
        p.newLen = BitConverter.ToInt64(b, q + 40); p.newSha = Sub(b, q + 48, 32);
        using (MemoryStream ms = new MemoryStream(b, q + 80, b.Length - q - 80))
        using (GZipStream gz = new GZipStream(ms, CompressionMode.Decompress))
        using (MemoryStream o = new MemoryStream()) { gz.CopyTo(o); p.ops = o.ToArray(); }
        return p;
    }
    static byte[] Sub(byte[] b, int o, int n) { byte[] r = new byte[n]; Array.Copy(b, o, r, 0, n); return r; }
    public byte[] Apply(byte[] orig)
    {
        byte[] res = new byte[newLen]; long w = 0; int i = 0;
        while (true)
        {
            byte t = ops[i++];
            if (t == 0) { long o = BitConverter.ToInt64(ops, i); int len = BitConverter.ToInt32(ops, i + 8); i += 12; Array.Copy(orig, o, res, w, len); w += len; }
            else if (t == 1) { int len = BitConverter.ToInt32(ops, i); i += 4; Array.Copy(ops, i, res, w, len); i += len; w += len; }
            else break;
        }
        if (w != newLen) throw new Exception("Patch result has the wrong size.");
        return res;
    }
}

static class Program
{
    const string BackupName = "ShoeMod_backup";
    const string RiolEntry = ",{\"assemblyName\":\"ShoeMod\",\"nameSpace\":\"\",\"className\":\"ShoeModLoader\",\"methodName\":\"Init\",\"loadTypes\":0,\"isUnityClass\":false}";
    static string here, game; static Tee tee;

    static int Main(string[] args)
    {
        try { Console.Title = "Skate Story In-Game Editor"; } catch (Exception) { }
        tee = new Tee(Console.Out); Console.SetOut(tee);
        Console.WriteLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "   Windows: " + Environment.OSVersion + "   .NET: " + Environment.Version);
        Console.WriteLine("Skate Story In-Game Editor 1.2.0 (in development) - installer");
        Console.WriteLine();
        try
        {
            here = AppDomain.CurrentDomain.BaseDirectory;
            string mode = null;
            foreach (string a in args) { if (a == "install" || a == "uninstall") mode = a; else if (IsGame(a)) game = a; }
            if (game == null) game = FindGame();
            while (game == null)
            {
                Console.WriteLine("The game folder was not found.");
                Console.WriteLine("In Steam: right-click Skate Story > Manage > Browse local files. Copy the folder path from the address bar.");
                Console.Write("Paste the folder path here and press Enter (or press Enter to stop): ");
                string s = Console.ReadLine(); if (s == null || s.Trim().Length == 0) return 1;
                s = s.Trim().Trim(new char[] { '"' }); if (IsGame(s)) game = s; else Console.WriteLine("SkateStory.exe is not in that folder.");
            }
            Console.WriteLine("Game folder: " + game);
            Console.WriteLine("Installer folder: " + here);
            Console.WriteLine();
            if (mode == null)
            {
                Console.WriteLine("  1 = Install or update the mod");
                Console.WriteLine("  2 = Remove the mod (put the original files back)");
                Console.Write("Type 1 or 2 and press Enter: ");
                string s = (Console.ReadLine() ?? "").Trim();
                Console.WriteLine("Answer: " + s);
                mode = s == "1" ? "install" : s == "2" ? "uninstall" : null;
                if (mode == null) { Console.WriteLine("Nothing was changed."); return Pause(1, args); }
            }
            if (GameRuns()) { Console.WriteLine("Skate Story is open. Close the game, then start this installer again."); return Pause(1, args); }
            bool ok = mode == "install" ? Install() : Uninstall();
            return Pause(ok ? 0 : 1, args);
        }
        catch (UnauthorizedAccessException e)
        {
            Console.WriteLine(); Console.WriteLine("Windows did not permit a change to a file: " + e.Message);
            Console.WriteLine("Right-click the installer and select \"Run as administrator\", then try again.");
            return Pause(1, args);
        }
        catch (Exception e) { Console.WriteLine(); Console.WriteLine("Error: " + e.GetType().Name + ": " + e.Message); return Pause(1, args); }
    }
    static int Pause(int code, string[] args)
    {
        Console.WriteLine("Result code: " + code);
        string logText = tee != null ? tee.log.ToString() : "";
        foreach (string dir in new[] { here, Path.GetTempPath() })
        {
            try { File.WriteAllText(Path.Combine(dir, "ShoeModInstaller.log"), logText); break; } catch (Exception) { }
        }
        if (args.Length == 0 || Array.IndexOf(args, "nopause") < 0) { Console.WriteLine(); Console.Write("Press Enter to close."); Console.ReadLine(); }
        return code;
    }
    static bool IsGame(string dir)
    {
        try { return dir != null && File.Exists(Path.Combine(dir, "SkateStory.exe")) && Directory.Exists(Path.Combine(dir, "SkateStory_Data")); } catch (Exception) { return false; }
    }
    static bool GameRuns() { try { return Process.GetProcessesByName("SkateStory").Length > 0; } catch (Exception) { return false; } }
    static string FindGame()
    {
        List<string> c = new List<string>();
        try { DirectoryInfo d = new DirectoryInfo(here); for (int i = 0; i < 4 && d != null; i++) { c.Add(d.FullName); d = d.Parent; } } catch (Exception) { }
        List<string> steam = new List<string>();
        foreach (string key in new[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam|InstallPath", @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam|InstallPath", @"HKEY_CURRENT_USER\Software\Valve\Steam|SteamPath" })
        {
            try { string[] k = key.Split(new char[] { '|' }); string v = Registry.GetValue(k[0], k[1], null) as string; if (v != null) steam.Add(v.Replace('/', '\\')); } catch (Exception) { }
        }
        steam.Add(@"C:\Program Files (x86)\Steam");
        foreach (string s in steam)
        {
            c.Add(Path.Combine(s, "steamapps", "common", "Skate Story"));
            try
            {   // other Steam libraries
                string vdf = Path.Combine(s, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (string line in File.ReadAllLines(vdf))
                    {
                        string t = line.Trim();
                        if (!t.StartsWith("\"path\"")) continue;
                        int a = t.IndexOf('"', 6), b = t.LastIndexOf('"');
                        if (a >= 0 && b > a) c.Add(Path.Combine(t.Substring(a + 1, b - a - 1).Replace("\\\\", "\\"), "steamapps", "common", "Skate Story"));
                    }
            }
            catch (Exception) { }
        }
        foreach (string d in c) if (IsGame(d)) return d;
        return null;
    }
    static byte[] Sha(byte[] b) { using (SHA256 s = SHA256.Create()) return s.ComputeHash(b); }
    static bool Same(byte[] a, byte[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    static List<Patch> Patches()
    {
        string dir = Path.Combine(here, "patches");
        if (!Directory.Exists(dir)) throw new Exception("The \"patches\" folder is not next to the installer. Extract the full ZIP file first.");
        List<Patch> l = new List<Patch>();
        foreach (string f in Directory.GetFiles(dir, "*.smp")) l.Add(Patch.Read(f));
        if (l.Count == 0) throw new Exception("No patch files found.");
        return l;
    }
    // Where an original copy of a game file can be: our backup folder (and the folder of an early test version).
    static string FindOriginal(Patch p)
    {
        foreach (string dn in new[] { BackupName, "_shoe_mod_backup" })
        {
            string f = Path.Combine(game, dn, Path.GetFileName(p.rel));
            try { if (File.Exists(f)) { byte[] b = File.ReadAllBytes(f); if (b.Length == p.origLen && Same(Sha(b), p.origSha)) return f; } } catch (Exception) { }
        }
        return null;
    }

    static bool Install()
    {
        string dll = Path.Combine(here, "ShoeMod.dll");
        if (!File.Exists(dll)) throw new Exception("ShoeMod.dll is not next to the installer. Extract the full ZIP file first.");
        string data = Path.Combine(game, "SkateStory_Data"), backup = Path.Combine(game, BackupName);
        string sa = Path.Combine(data, "ScriptingAssemblies.json"), ri = Path.Combine(data, "RuntimeInitializeOnLoads.json");
        if (!File.Exists(sa) || !File.Exists(ri)) throw new Exception("The game files ScriptingAssemblies.json and RuntimeInitializeOnLoads.json were not found.");

        // 1. check all files first, so that the game is not left half changed
        List<Patch> patches = Patches();
        List<byte[]> results = new List<byte[]>(); List<byte[]> originals = new List<byte[]>();
        foreach (Patch p in patches)
        {
            string target = Path.Combine(game, p.rel);
            if (!File.Exists(target)) throw new Exception("Game file not found: " + p.rel);
            byte[] cur = File.ReadAllBytes(target); byte[] sha = Sha(cur);
            if (Same(sha, p.newSha)) { results.Add(null); originals.Add(null); continue; }
            byte[] orig = null;
            if (cur.Length == p.origLen && Same(sha, p.origSha)) orig = cur;
            else { string f = FindOriginal(p); if (f != null) orig = File.ReadAllBytes(f); }
            if (orig == null)
            {
                Console.WriteLine("This game file is not the version that the mod was made for:");
                Console.WriteLine("  " + p.rel);
                Console.WriteLine("Possible causes: a game update, or a different mod changed the file.");
                Console.WriteLine("In Steam, use Properties > Installed Files > Verify integrity of game files, then try again.");
                Console.WriteLine("If the game had an update, this mod needs an update too. Nothing was changed.");
                return false;
            }
            byte[] res = p.Apply(orig);
            if (!Same(Sha(res), p.newSha)) throw new Exception("The patch result is not correct for " + Path.GetFileName(p.rel) + ". Nothing was changed.");
            results.Add(res); originals.Add(orig);
        }
        string saText = File.ReadAllText(sa), riText = File.ReadAllText(ri);
        string saNew = AddAssembly(saText), riNew = AddLoader(riText);

        // 2. make backups, then write
        Directory.CreateDirectory(backup);
        for (int i = 0; i < patches.Count; i++)
        {
            Patch p = patches[i]; string name = Path.GetFileName(p.rel);
            if (results[i] == null) { Console.WriteLine("Already done:  " + name); continue; }
            string bf = Path.Combine(backup, name);
            if (!File.Exists(bf) || !Same(Sha(File.ReadAllBytes(bf)), p.origSha)) File.WriteAllBytes(bf, originals[i]);
            File.WriteAllBytes(Path.Combine(game, p.rel), results[i]);
            Console.WriteLine("Changed:       " + name);
        }
        if (saNew != saText) { BackupOnce(sa, backup); File.WriteAllText(sa, saNew); Console.WriteLine("Changed:       ScriptingAssemblies.json"); }
        if (riNew != riText) { BackupOnce(ri, backup); File.WriteAllText(ri, riNew); Console.WriteLine("Changed:       RuntimeInitializeOnLoads.json"); }
        File.Copy(dll, Path.Combine(data, "Managed", "ShoeMod.dll"), true);
        Console.WriteLine("Copied:        ShoeMod.dll");
        // read the files again from the disk to make sure that the changes are there
        foreach (Patch p in patches)
            if (!Same(Sha(File.ReadAllBytes(Path.Combine(game, p.rel))), p.newSha)) throw new Exception("After the install, this file is not correct on the disk: " + Path.GetFileName(p.rel));
        if (new FileInfo(Path.Combine(data, "Managed", "ShoeMod.dll")).Length != new FileInfo(dll).Length) throw new Exception("ShoeMod.dll was not copied correctly.");
        Console.WriteLine("Checked:       all files are correct on the disk");
        Console.WriteLine();
        Console.WriteLine("The mod is installed. Start the game and press F6 to open the editor.");
        Console.WriteLine("The original files are in: " + backup);
        return true;
    }
    static void BackupOnce(string file, string backup)
    {
        string bf = Path.Combine(backup, Path.GetFileName(file));
        if (!File.Exists(bf)) File.Copy(file, bf);
    }
    // ScriptingAssemblies.json holds two lists of the same length: "names" and "types".
    static bool Lists(string t, out int n0, out int n1, out int t0, out int t1)
    {
        n0 = n1 = t0 = t1 = -1;
        int a = t.IndexOf("\"names\""); if (a < 0) return false; n0 = t.IndexOf('[', a); n1 = t.IndexOf(']', n0);
        int b = t.IndexOf("\"types\""); if (b < 0) return false; t0 = t.IndexOf('[', b); t1 = t.IndexOf(']', t0);
        return n0 >= 0 && n1 > n0 && t0 >= 0 && t1 > t0;
    }
    static string AddAssembly(string t)
    {
        if (t.Contains("\"ShoeMod.dll\"")) return t;
        int n0, n1, t0, t1; if (!Lists(t, out n0, out n1, out t0, out t1)) throw new Exception("ScriptingAssemblies.json has a format that the installer does not know. Nothing was changed.");
        // put the later one in first, so that the earlier position stays correct
        if (t1 > n1) { t = t.Insert(t1, ",16"); t = t.Insert(n1, ",\"ShoeMod.dll\""); }
        else { t = t.Insert(n1, ",\"ShoeMod.dll\""); t = t.Insert(t1, ",16"); }
        return t;
    }
    static string RemoveAssembly(string t)
    {
        if (!t.Contains("\"ShoeMod.dll\"")) return t;
        int n0, n1, t0, t1; if (!Lists(t, out n0, out n1, out t0, out t1)) return t;
        List<string> names = new List<string>(t.Substring(n0 + 1, n1 - n0 - 1).Split(new char[] { ',' }));
        List<string> types = new List<string>(t.Substring(t0 + 1, t1 - t0 - 1).Split(new char[] { ',' }));
        int idx = names.FindIndex(delegate (string s) { return s.Trim() == "\"ShoeMod.dll\""; });
        if (idx < 0 || names.Count != types.Count) return t;
        names.RemoveAt(idx); types.RemoveAt(idx);
        string nn = string.Join(",", names.ToArray()), tt = string.Join(",", types.ToArray());
        if (t1 > n1) { t = t.Substring(0, t0 + 1) + tt + t.Substring(t1); t = t.Substring(0, n0 + 1) + nn + t.Substring(n1); }
        else { t = t.Substring(0, n0 + 1) + nn + t.Substring(n1); t = t.Substring(0, t0 + 1) + tt + t.Substring(t1); }
        return t;
    }
    static string AddLoader(string t)
    {
        if (t.Contains("\"ShoeModLoader\"")) return t;
        int i = t.LastIndexOf("]}");
        if (i < 0) throw new Exception("RuntimeInitializeOnLoads.json has a format that the installer does not know. Nothing was changed.");
        return t.Insert(i, RiolEntry);
    }

    static bool Uninstall()
    {
        string data = Path.Combine(game, "SkateStory_Data");
        bool clean = true;
        foreach (Patch p in Patches())
        {
            string target = Path.Combine(game, p.rel), name = Path.GetFileName(p.rel);
            if (!File.Exists(target)) continue;
            byte[] sha = Sha(File.ReadAllBytes(target));
            if (Same(sha, p.origSha)) { Console.WriteLine("Is original:   " + name); continue; }
            string f = FindOriginal(p);
            if (f != null) { File.Copy(f, target, true); Console.WriteLine("Put back:      " + name); }
            else { clean = false; Console.WriteLine("No backup for: " + name); }
        }
        string sa = Path.Combine(data, "ScriptingAssemblies.json"), ri = Path.Combine(data, "RuntimeInitializeOnLoads.json");
        if (File.Exists(sa)) { string t = File.ReadAllText(sa), n = RemoveAssembly(t); if (n != t) { File.WriteAllText(sa, n); Console.WriteLine("Changed back:  ScriptingAssemblies.json"); } }
        if (File.Exists(ri)) { string t = File.ReadAllText(ri), n = t.Replace(RiolEntry, ""); if (n != t) { File.WriteAllText(ri, n); Console.WriteLine("Changed back:  RuntimeInitializeOnLoads.json"); } }
        string dll = Path.Combine(data, "Managed", "ShoeMod.dll");
        if (File.Exists(dll)) { File.Delete(dll); Console.WriteLine("Deleted:       ShoeMod.dll"); }
        Console.WriteLine();
        if (clean) Console.WriteLine("The mod is removed. Your saved designs and settings stay on this computer and come back if you install the mod again.");
        else Console.WriteLine("Some files had no backup. In Steam, use Properties > Installed Files > Verify integrity of game files to get the original files.");
        return clean;
    }
}
