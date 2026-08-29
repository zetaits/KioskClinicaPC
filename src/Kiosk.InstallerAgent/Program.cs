using Kiosk.InstallerAgent;

Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "KioskClinicaPC Installer Agent")
    .ConfigureServices(services => services.AddHostedService<InstallerWorker>())
    .Build()
    .Run();
