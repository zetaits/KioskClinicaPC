using System;
using System.IO;
using System.Windows;
using KioskClinicaPC.Core; 
using KioskClinicaPC.Windows; 
using Newtonsoft.Json; 

namespace KioskClinicaPC
{
    public partial class App : Application
    {
        public static readonly string AppDataFolderName = "KioskClinicaPC";
        public static readonly string AppDataFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            AppDataFolderName
        );
        
        public static readonly string ConfigFilePath = Path.Combine(AppDataFolderPath, "KioskConfig.json");
        public static readonly string HardwareFilePath = Path.Combine(AppDataFolderPath, "KioskHardware.json");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Directory.CreateDirectory(AppDataFolderPath);

            if (!File.Exists(ConfigFilePath))
            {
                var configDialog = new FirstRunConfigWindow();
                bool? result = configDialog.ShowDialog();

                if (result == true)
                {
                    var config = configDialog.ConfigData;
                    string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                    File.WriteAllText(ConfigFilePath, json);
                }
                else
                {
                    Application.Current.Shutdown();
                    return; 
                }
            }
            
            var mainWindow = new MainWindow();
            Application.Current.MainWindow = mainWindow; 
            mainWindow.Show();
        }
    }
}