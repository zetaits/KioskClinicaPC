using System.Windows;

namespace Kiosk.DeploymentManager;
internal static class Program
{
    [STAThread] public static int Main() => new Application().Run(new MainWindow());
}
