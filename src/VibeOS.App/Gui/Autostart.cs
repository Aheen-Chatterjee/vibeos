namespace VibeOS.App.Gui;

/// <summary>
/// "Start with Windows" via a Startup-folder shortcut (late-bound COM, no
/// interop reference). Target is always this exe with --tray.
/// </summary>
public static class Autostart
{
    private const string LinkName = "VibeOS.lnk";

    private static string LinkPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), LinkName);

    private static string OwnExe =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "VibeOS.exe");

    public static bool IsEnabled()
    {
        try
        {
            if (!File.Exists(LinkPath)) return false;
            dynamic shell = Activator.CreateInstance(
                Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic link = shell.CreateShortcut(LinkPath);
            string target = link.TargetPath as string ?? string.Empty;
            return string.Equals(
                Path.GetFullPath(target), Path.GetFullPath(OwnExe),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            dynamic shell = Activator.CreateInstance(
                Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic link = shell.CreateShortcut(LinkPath);
            link.TargetPath = OwnExe;
            link.Arguments = "--tray";
            link.WorkingDirectory = AppContext.BaseDirectory;
            link.Description = "VibeOS controller input (starts hidden in tray)";
            link.Save();
        }
        else if (File.Exists(LinkPath))
        {
            File.Delete(LinkPath);
        }
    }
}
