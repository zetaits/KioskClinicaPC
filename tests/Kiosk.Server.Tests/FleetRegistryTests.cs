using Kiosk.Server.Services;
using KioskClinicaPC.Core.Sync;
using Xunit;

namespace Kiosk.Server.Tests;

public class FleetRegistryTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kiosk-fleet-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static FleetRegistry NewRegistry(string? dir = null) => new(dir ?? TempDir(), TimeZoneInfo.Utc);

    private static KioskHeartbeat Hb(string id = "d1", string name = "PC1", KioskScreen screen = KioskScreen.Main,
        decimal price = 1000, decimal oldPrice = 0) =>
        new() { DeviceId = id, Name = name, Screen = screen, Price = price, OldPrice = oldPrice, Cpu = "CPU", Equipment = "EQ" };

    [Fact]
    public void Upsert_registers_device_online()
    {
        var reg = NewRegistry();
        var pending = reg.Upsert("conn1", "1.2.3.4", Hb());

        Assert.Empty(pending);
        var d = reg.Find("d1");
        Assert.NotNull(d);
        Assert.True(d!.IsOnline);
        Assert.Equal("PC1", d.Name);
        Assert.Equal("conn1", d.ConnectionId);
        Assert.Equal("1.2.3.4", d.Ip);
        Assert.Equal(1, reg.OnlineCount);
    }

    [Fact]
    public void MarkOffline_drops_device()
    {
        var reg = NewRegistry();
        reg.Upsert("conn1", "", Hb());
        reg.MarkOffline("conn1");

        var d = reg.Find("d1");
        Assert.NotNull(d);
        Assert.False(d!.IsOnline);
        Assert.Null(d.ConnectionId);
        Assert.Equal(1, reg.OfflineCount);
    }

    [Fact]
    public void Busy_when_showing_detail_or_scan()
    {
        var reg = NewRegistry();
        reg.Upsert("c", "", Hb(screen: KioskScreen.Detail));
        Assert.Equal(KioskStatus.Busy, reg.Find("d1")!.Status);
    }

    [Fact]
    public void Rename_overrides_reported_name_and_queues_until_applied()
    {
        var reg = NewRegistry();
        reg.Upsert("c1", "", Hb(name: "PC1"));

        var connId = reg.Rename("d1", "MOSTRADOR-01");
        Assert.Equal("c1", connId);
        Assert.Equal("MOSTRADOR-01", reg.Find("d1")!.Name);

        // Reconecta reportando aún el nombre viejo → hay que reenviarle el SetName.
        var pending = reg.Upsert("c2", "", Hb(name: "PC1"));
        Assert.Contains(pending, c => c.Kind == FleetCommandKind.SetName && c.Name == "MOSTRADOR-01");

        // Cuando ya reporta el nombre del override, no se reenvía.
        var pending2 = reg.Upsert("c2", "", Hb(name: "MOSTRADOR-01"));
        Assert.DoesNotContain(pending2, c => c.Kind == FleetCommandKind.SetName);
    }

    [Fact]
    public void SetPriceOverride_queues_until_device_reports_it()
    {
        var reg = NewRegistry();
        reg.Upsert("c1", "", Hb(price: 1000));

        var connId = reg.SetPriceOverride("d1", 899, 1099);
        Assert.Equal("c1", connId);

        // Todavía reporta el precio viejo → pendiente de aplicar.
        var pending = reg.Upsert("c1", "", Hb(price: 1000));
        Assert.Contains(pending, c => c.Kind == FleetCommandKind.SetPrice && c.Price == 899 && c.OldPrice == 1099);

        // Ya reporta el precio nuevo → no se reenvía.
        var pending2 = reg.Upsert("c1", "", Hb(price: 899, oldPrice: 1099));
        Assert.DoesNotContain(pending2, c => c.Kind == FleetCommandKind.SetPrice);
    }

    [Fact]
    public void Overrides_persist_across_instances()
    {
        string dir = TempDir();
        var reg = NewRegistry(dir);
        reg.Rename("d1", "PERSISTENTE"); // override incluso sin el equipo conectado

        var reg2 = NewRegistry(dir);
        var pending = reg2.Upsert("c", "", Hb(name: "reportado"));
        Assert.Contains(pending, c => c.Kind == FleetCommandKind.SetName && c.Name == "PERSISTENTE");
        Assert.Equal("PERSISTENTE", reg2.Find("d1")!.Name);
    }

    [Fact]
    public void AveragePrice_ignores_unpriced_devices()
    {
        var reg = NewRegistry();
        reg.Upsert("c1", "", Hb(id: "a", price: 1000));
        reg.Upsert("c2", "", Hb(id: "b", price: 2000));
        reg.Upsert("c3", "", Hb(id: "c", price: 0));
        Assert.Equal(1500, reg.AveragePrice);
    }
}
