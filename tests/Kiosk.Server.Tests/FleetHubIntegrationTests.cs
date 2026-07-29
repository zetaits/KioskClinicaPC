using Kiosk.Server.Hubs;
using Kiosk.Server.Services;
using KioskClinicaPC.Core.Sync;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiosk.Server.Tests;

/// <summary>
/// Levanta el servidor real en memoria y comprueba el ciclo de flota de punta a punta: un cliente SignalR
/// se registra en <c>/hub/fleet</c>, aparece online en el <see cref="FleetRegistry"/>, y una orden del panel
/// (por <see cref="IHubContext{FleetHub}"/>) llega al cliente. Con ApiKey vacía = servidor abierto (sin
/// guardia), suficiente para el test.
/// </summary>
public sealed class FleetHubIntegrationTests : IAsyncLifetime
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "kiosk-hub-tests", Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_dataDir);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Kiosk:DataDir", _dataDir);
            builder.UseSetting("Kiosk:AssetsDir", Path.Combine(_dataDir, "assets"));
            builder.UseSetting("Kiosk:ApiKey", ""); // abierto para el test
        });
        _ = _factory.Server; // fuerza el arranque
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        try { Directory.Delete(_dataDir, recursive: true); } catch { }
        return Task.CompletedTask;
    }

    private HubConnection BuildClient()
    {
        var handler = _factory.Server.CreateHandler();
        return new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hub/fleet"),
                o => o.HttpMessageHandlerFactory = _ => handler)
            .Build();
    }

    [Fact]
    public async Task Register_makes_device_appear_online_and_command_reaches_client()
    {
        var received = new TaskCompletionSource<FleetCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        var conn = BuildClient();
        conn.On<FleetCommand>("Command", cmd => received.TrySetResult(cmd));

        await conn.StartAsync();
        await conn.InvokeAsync("Register", new KioskHeartbeat
        {
            DeviceId = "kiosk-1",
            Name = "MOSTRADOR-01",
            Screen = KioskScreen.Main,
            Price = 1299,
        });

        var registry = _factory.Services.GetRequiredService<FleetRegistry>();
        var device = registry.Find("kiosk-1");
        Assert.NotNull(device);
        Assert.True(device!.IsOnline);
        Assert.Equal("MOSTRADOR-01", device.Name);
        Assert.NotNull(device.ConnectionId);

        // El panel manda una orden dirigida al equipo por su ConnectionId.
        var hub = _factory.Services.GetRequiredService<IHubContext<FleetHub>>();
        await hub.Clients.Client(device.ConnectionId!).SendAsync("Command", FleetCommand.RestartApp());

        var delivered = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(delivered == received.Task, "El cliente no recibió la orden a tiempo.");
        var cmd = await received.Task;
        Assert.Equal(FleetCommandKind.RestartApp, cmd.Kind);

        await conn.DisposeAsync();
    }

    [Fact]
    public async Task Disconnect_marks_device_offline()
    {
        var conn = BuildClient();
        await conn.StartAsync();
        await conn.InvokeAsync("Register", new KioskHeartbeat { DeviceId = "kiosk-2", Name = "PC2", Screen = KioskScreen.Attract });

        var registry = _factory.Services.GetRequiredService<FleetRegistry>();
        Assert.True(registry.Find("kiosk-2")!.IsOnline);

        await conn.DisposeAsync();

        // La desconexión limpia dispara OnDisconnectedAsync → MarkOffline. Da un margen breve.
        FleetDevice? d = null;
        for (int i = 0; i < 50 && (d = registry.Find("kiosk-2")) is { IsOnline: true }; i++)
            await Task.Delay(100);

        Assert.NotNull(d);
        Assert.False(d!.IsOnline);
    }
}
