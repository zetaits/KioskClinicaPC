using System.IO;
using System.IO.Compression;
using Kiosk.EquipmentSetup;
using Xunit;
namespace Kiosk.EquipmentSetup.Tests;
public sealed class PayloadTests
{
    private static MemoryStream Archive(string path, bool winmd = true)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (string name in new[] { "KioskSetupHelper.exe", "KioskSetupHelper.dll", "KioskSetupHelper.runtimeconfig.json" })
            { using var entry = archive.CreateEntry(name).Open(); entry.WriteByte(1); }
            if (winmd) { using var entry = archive.CreateEntry("Microsoft.Management.Deployment.winmd").Open(); entry.WriteByte(1); }
            using var extra = archive.CreateEntry(path).Open(); extra.WriteByte(1);
        }
        stream.Position = 0; return stream;
    }
    [Fact]
    public void Canonical_worker_with_physical_winmd_is_accepted()
    {
        using var stream = Archive("es/PresentationCore.resources.dll"); Assert.True(Payload.ValidateWorkerArchive(stream));
    }
    [Theory]
    [InlineData("../escaped.exe")]
    [InlineData("C:/escaped.exe")]
    [InlineData("/escaped.exe")]
    [InlineData("es\\PresentationCore.resources.dll")]
    [InlineData("es./file.dll")]
    [InlineData("kiosksetuphelper.exe")]
    public void Malformed_archive_paths_or_duplicate_names_are_rejected_before_extraction(string path)
    {
        using var stream = Archive(path); Assert.False(Payload.ValidateWorkerArchive(stream));
    }
    [Fact]
    public void Missing_winmd_is_not_a_usable_worker()
    {
        using var stream = Archive("es/file.dll", false); Assert.False(Payload.ValidateWorkerArchive(stream));
    }
}
