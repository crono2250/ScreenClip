using System.Reflection;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

[assembly: AssemblyInformationalVersion("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
internal static class UpdateFixture
{
    private static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--test-parent") { Thread.Sleep(500); return; }
        if (args.Length != 2 || args[0] != "--updated") return;
        string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (File.Exists(Path.Combine(directory, "fail-startup"))) return;
        string stage = File.ReadAllText(Path.Combine(directory, "update-test-stage.txt"));
        using (Process process = Process.GetCurrentProcess())
            File.WriteAllText(Path.Combine(stage, "ready"), process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Thread.Sleep(10000);
    }
}
