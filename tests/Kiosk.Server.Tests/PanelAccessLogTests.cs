using Kiosk.Server.Services;
using Xunit;

namespace Kiosk.Server.Tests;

public class PanelAccessLogTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kiosk-access-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Records_newest_first()
    {
        var log = new PanelAccessLog(TempDir());
        log.Record("1.1.1.1", AccessOutcome.Fail);
        log.Record("2.2.2.2", AccessOutcome.Success);

        var recent = log.Recent(10);
        Assert.Equal(2, recent.Count);
        Assert.Equal(AccessOutcome.Success, recent[0].Outcome);
        Assert.Equal("2.2.2.2", recent[0].Ip);
        Assert.Equal(AccessOutcome.Fail, recent[1].Outcome);
    }

    [Fact]
    public void Blank_ip_stored_as_placeholder()
    {
        var log = new PanelAccessLog(TempDir());
        log.Record("  ", AccessOutcome.Locked);
        Assert.Equal("desconocida", log.Recent(1)[0].Ip);
    }

    [Fact]
    public void Persists_across_instances()
    {
        string dir = TempDir();
        new PanelAccessLog(dir).Record("9.9.9.9", AccessOutcome.Locked);

        var recent = new PanelAccessLog(dir).Recent(10);
        Assert.Single(recent);
        Assert.Equal(AccessOutcome.Locked, recent[0].Outcome);
        Assert.Equal("9.9.9.9", recent[0].Ip);
    }
}
