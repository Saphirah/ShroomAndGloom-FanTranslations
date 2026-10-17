using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Common;

// Offline metadata analysis only. Never loads or executes the game executable.
var game = Path.GetFullPath(args[0]);
var loader = Path.GetFullPath(args[1]);
Paths.SetExecutablePath(Path.Combine(game, "Shroom and Gloom.exe"), Path.Combine(loader, "BepInEx"));
Directory.CreateDirectory(Paths.ConfigPath);
Logger.Listeners.Add(new StdoutListener());
typeof(UnityInfo).GetMethod("Initialize", BindingFlags.NonPublic | BindingFlags.Static)!
    .Invoke(null, new object[] { Path.Combine(game, "UnityPlayer.dll"), Paths.GameDataPath });
Console.WriteLine($"Offline interop generation for Unity {UnityInfo.Version}");
if (args.Length > 2 && args[2] is "--inspect" or "--find")
{
    AppDomain.CurrentDomain.AssemblyResolve += (_, e) => {
        var p = Path.Combine(loader, "BepInEx/interop", new AssemblyName(e.Name).Name + ".dll");
        return File.Exists(p) ? Assembly.LoadFrom(p) : null;
    };
    foreach (var dll in new[] { "Assembly-CSharp", "Unity.Localization", "Unity.TextMeshPro" })
    {
        var interop = Assembly.LoadFrom(Path.Combine(loader, "BepInEx/interop", dll + ".dll"));
        foreach (var type in interop.GetTypes().Where(t => args.Skip(3).Any(n => args[2] == "--find" ? t.FullName!.Contains(n, StringComparison.OrdinalIgnoreCase) : t.FullName == n)))
        {
            Console.WriteLine("TYPE " + type.FullName);
            if (args[2] == "--find") continue;
            foreach (var m in type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (!m.Name.StartsWith("Native")) Console.WriteLine(m);
        }
    }
    return 0;
}
var assembly = Assembly.Load("BepInEx.Unity.IL2CPP");
assembly.GetType("BepInEx.Unity.IL2CPP.Il2CppInteropManager")!
    .GetMethod("GenerateInteropAssemblies", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
if (!File.Exists(Path.Combine(loader, "BepInEx/interop/Assembly-CSharp.dll"))) return 1;
return 0;

sealed class StdoutListener : ILogListener
{
    public LogLevel LogLevelFilter => LogLevel.Info | LogLevel.Warning | LogLevel.Error | LogLevel.Message;
    public void LogEvent(object sender, LogEventArgs e) { if ((e.Level & LogLevelFilter) != 0) Console.WriteLine($"[{e.Level}] {e.Data}"); }
    public void Dispose() { }
}
