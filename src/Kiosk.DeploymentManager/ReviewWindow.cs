using System.Windows;
using System.Windows.Controls;
using Kiosk.Deployment;

namespace Kiosk.DeploymentManager;

internal sealed class ReviewWindow : Window
{
    internal Dictionary<string, string> Passwords { get; } = [];
    internal ReviewWindow(List<TargetRow> targets, DeploymentProfile profile, DeploymentImage? image)
    {
        Title = "Revisión final · Instalación limpia"; Width = 860; Height = 700; MinWidth = 700; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Resources = new ResourceDictionary { Source = new Uri("pack://application:,,,/Kiosk.DeploymentManager;component/SetupTheme.xaml") };
        Background = (System.Windows.Media.Brush)Resources["PageBrush"]; Foreground = (System.Windows.Media.Brush)Resources["TextBrush"];
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Se borrarán todas las particiones del disco seleccionado de cada equipo.", FontSize = 22, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"Perfil {profile.Name} · Revisión {profile.Revision}\n{image?.Name} · {image?.Editions.Find(e => e.Index == profile.EditionIndex)?.Name}\nAplicaciones: {string.Join(", ", profile.Applications.Applications.Select(a => a.DisplayName + " " + a.PinnedVersion))}\nKiosk: {(profile.Kiosk ? "Sí; siguiente inicio de sesión" : "No")}", Margin = new Thickness(0, 16, 0, 16), TextWrapping = TextWrapping.Wrap });
        var rows = new List<(TargetRow Row, TextBox User, PasswordBox Password)>();
        var commonUser = new TextBox { Text = profile.Username, Margin = new Thickness(0, 8, 0, 8) };
        panel.Children.Add(new TextBlock { Text = "Usuario común (respeta los usuarios pendientes fijados en el panel)" }); panel.Children.Add(commonUser);
        var apply = new Button { Content = "Aplicar usuario común", Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(apply);
        foreach (var target in targets)
        {
            panel.Children.Add(new TextBlock { Text = $"{target.Label}\nSerie {target.Serial}\nDisco {target.Disk!.Label}", FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) });
            panel.Children.Add(new TextBlock { Text = "Nombre de usuario" });
            var user = new TextBox { Text = target.Username, MaxLength = 20, IsReadOnly = target.Session.PendingUsername is not null }; panel.Children.Add(user);
            panel.Children.Add(new TextBlock { Text = "Contraseña para este equipo (vacía: predeterminada protegida en la estación)", Margin = new Thickness(0, 8, 0, 4) });
            var password = new PasswordBox(); panel.Children.Add(password); rows.Add((target, user, password));
        }
        apply.Click += (_, _) => { foreach (var row in rows.Where(r => r.Row.Session.PendingUsername is null)) row.User.Text = commonUser.Text; };
        var acknowledgement = new CheckBox { Content = "He comprobado físicamente los equipos y los discos. Autorizo borrar los discos indicados.", Margin = new Thickness(0, 24, 0, 12) }; panel.Children.Add(acknowledgement);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick }; panel.Children.Add(error);
        var confirm = new Button { Content = "Confirmar instalación del lote", IsEnabled = false, MinHeight = 42 }; panel.Children.Add(confirm);
        acknowledgement.Checked += (_, _) => confirm.IsEnabled = true; acknowledgement.Unchecked += (_, _) => confirm.IsEnabled = false;
        confirm.Click += (_, _) =>
        {
            try
            {
                foreach (var row in rows)
                {
                    DeploymentPolicy.Username(row.User.Text); row.Row.Username = row.User.Text;
                    if (row.Password.Password.Length > 0)
                    { DeploymentPolicy.Require(row.Password.Password.Length is >= 8 and <= 128 && !row.Password.Password.Any(char.IsControl), "La contraseña debe tener entre 8 y 128 caracteres."); Passwords[row.Row.Session.Id] = row.Password.Password; }
                }
                DialogResult = true;
            }
            catch (System.IO.InvalidDataException ex) { error.Text = ex.Message; }
        };
    }
}
