using Kiosk.SetupHelper;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => LegacyCommands.Run(args);
}
