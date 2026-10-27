using System.Reflection;
using Meridian.TaskLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // No console is allocated for this WinExe, including rejected arguments.
        if (args.Length != 1 || args[0] is not ("backend" or "secure-tunnel" or "setup")) return 64;
        try
        {
            if (args[0] == "setup") return KeySetup.ShowDialog();
            var metadata = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                .ToDictionary(a => a.Key, a => a.Value ?? "", StringComparer.Ordinal);
            var plan = TaskLaunch.Create(args[0], metadata);
            return TaskLaunch.StartAndWait(plan);
        }
        catch
        {
            // Do not display dialogs or echo arbitrary errors that may contain local metadata.
            return 70;
        }
    }
}
