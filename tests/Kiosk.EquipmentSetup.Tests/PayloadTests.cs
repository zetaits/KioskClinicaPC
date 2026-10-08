using System.IO;
using System.IO.Compression;
using Kiosk.EquipmentSetup;
using KioskClinicaPC.Equipment;
using Xunit;
namespace Kiosk.EquipmentSetup.Tests;
[Collection("Equipment resources")]
public sealed class PayloadTests
{
    private static MemoryStream Archive(string path, bool winmd = true, int? catalogVersion = 3)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (string name in new[] { "KioskSetupHelper.exe", "KioskSetupHelper.dll", "KioskSetupHelper.runtimeconfig.json" })
            { using var entry = archive.CreateEntry(name).Open(); entry.WriteByte(1); }
            if (winmd) { using var entry = archive.CreateEntry("Microsoft.Management.Deployment.winmd").Open(); entry.WriteByte(1); }
            if (catalogVersion is not null)
            {
                using var metadata = archive.CreateEntry("pack-worker.json").Open();
                System.Text.Json.JsonSerializer.Serialize(metadata, new PackWorkerManifest(1, catalogVersion.Value, "1.4.0", new string('a', 40)), Payload.Json);
            }
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
    [InlineData("nul.exe")]
    [InlineData("con/file.dll")]
    [InlineData("a//file.dll")]
    [InlineData("a/fi?le.dll")]
    [InlineData("KioskSetupHelper.exe/nested.dll")]
    public void Malformed_archive_paths_or_duplicate_names_are_rejected_before_extraction(string path)
    {
        using var stream = Archive(path); Assert.False(Payload.ValidateWorkerArchive(stream));
    }
    [Fact]
    public void Old_worker_or_missing_capabilities_is_rejected_before_extraction()
    {
        using var old = Archive("es/file.dll", catalogVersion: 2); Assert.False(Payload.ValidateWorkerArchive(old));
        using var missing = Archive("es/file.dll", catalogVersion: null); Assert.False(Payload.ValidateWorkerArchive(missing));
    }
    [Fact]
    public void Missing_winmd_is_not_a_usable_worker()
    {
        using var stream = Archive("es/file.dll", false); Assert.False(Payload.ValidateWorkerArchive(stream));
    }
    private sealed class NoNetwork : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken ct) =>
            throw new Exception("Local deployment must never use the remote provider.");
    }
    [Theory]
    [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task Directory_source_prepares_only_selected_components_without_network(bool pack, bool kiosk)
    {
        string root = Path.Combine(Path.GetTempPath(), "equipment-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); string work = Path.Combine(root, "work"); Directory.CreateDirectory(work);
        try
        {
            using var zip = Archive("extra.dll");
            File.WriteAllBytes(Path.Combine(root, "worker.zip"), zip.ToArray()); File.WriteAllBytes(Path.Combine(root, "kiosk.exe"), [1, 2, 3]);
            string Hash(string name) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root, name)))).ToLowerInvariant();
            var manifest = new PayloadManifest(2, 3, "equipment-wpf", "0.1.0", "0.1.0", "1.2.0", new string('0', 40), Hash("worker.zip"), Hash("kiosk.exe"));
            File.WriteAllText(Path.Combine(root, "payload.json"), System.Text.Json.JsonSerializer.Serialize(manifest, Payload.Json));
            Payload.UseDirectory(root);
            using var http = new System.Net.Http.HttpClient(new NoNetwork());
            await Payload.Prepare(new(pack, kiosk, 0, [], false), work, http, _ => { }, default);
            Assert.Equal(pack, File.Exists(Path.Combine(work, "worker.zip"))); Assert.Equal(kiosk, File.Exists(Path.Combine(work, "kiosk.exe")));
        }
        finally { Payload.UseAssembly(typeof(MainWindow).Assembly); Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Corrupt_local_kiosk_stops_both_before_execution_and_prepared_file_is_reverified()
    {
        string root = Path.Combine(Path.GetTempPath(), "equipment-corrupt-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string work = Path.Combine(root, "work"); Directory.CreateDirectory(work);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "kiosk.exe"), [1, 2, 3]);
            var manifest = new PayloadManifest(2, 3, "equipment-wpf", "0.1.0", "0.1.0", "1.2.0", new string('0', 40), new string('0', 64), new string('0', 64));
            File.WriteAllText(Path.Combine(root, "payload.json"), System.Text.Json.JsonSerializer.Serialize(manifest, Payload.Json)); Payload.UseDirectory(root);
            using var http = new System.Net.Http.HttpClient(new NoNetwork());
            var error = await Assert.ThrowsAsync<ComponentPreparationException>(() => Payload.Prepare(new(false, true, 0, [], false), work, http, _ => { }, default));
            Assert.Contains("Kiosk", error.Message);
            await Assert.ThrowsAsync<InvalidDataException>(() => Payload.Extract("kiosk.exe", manifest.KioskSha256, work, _ => { }, default));
        }
        finally { Payload.UseAssembly(typeof(MainWindow).Assembly); Directory.Delete(root, true); }
    }
    [Fact]
    public void Symlink_entries_and_excessive_entry_count_are_rejected()
    {
        using var stream = Archive("link.dll");
        using (var update = new ZipArchive(stream, ZipArchiveMode.Update, true)) update.GetEntry("link.dll")!.ExternalAttributes = unchecked((int)0xA0000000);
        stream.Position = 0; Assert.False(Payload.ValidateWorkerArchive(stream));
        using var many = new MemoryStream();
        using (var archive = new ZipArchive(many, ZipArchiveMode.Create, true))
            for (int i = 0; i < 2001; i++) archive.CreateEntry($"{i}.dll");
        many.Position = 0; Assert.False(Payload.ValidateWorkerArchive(many));
    }
    [Fact]
    public void Excessive_declared_expansion_is_rejected_without_extracting()
    {
        using var original = Archive("oversized.dll"); byte[] bytes = original.ToArray();
        for (int i = 0; i < bytes.Length - 46; i++)
        {
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i, 4)) != 0x02014b50) continue;
            int length = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i + 28, 2));
            if (System.Text.Encoding.UTF8.GetString(bytes, i + 46, length) == "oversized.dll")
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i + 24, 4), 0x50000000);
        }
        using var oversized = new MemoryStream(bytes); Assert.False(Payload.ValidateWorkerArchive(oversized));
    }
    [Fact]
    public void Forged_metadata_length_does_not_bypass_bounded_read()
    {
        using var original = Archive("extra.dll");
        using (var archive = new ZipArchive(original, ZipArchiveMode.Update, true))
        {
            archive.GetEntry("pack-worker.json")!.Delete();
            using var metadata = archive.CreateEntry("pack-worker.json").Open();
            metadata.Write(System.Text.Encoding.UTF8.GetBytes(new string(' ', 5000) + "{}"));
        }
        byte[] bytes = original.ToArray();
        for (int i = 0; i < bytes.Length - 46; i++)
        {
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i, 4)) != 0x02014b50) continue;
            int length = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i + 28, 2));
            if (System.Text.Encoding.UTF8.GetString(bytes, i + 46, length) == "pack-worker.json")
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i + 24, 4), 100);
        }
        using var forged = new MemoryStream(bytes); Assert.False(Payload.ValidateWorkerArchive(forged));
    }
    [Fact]
    public async Task Resource_diagnosis_rejects_a_correct_hash_with_wrong_declared_size()
    {
        string root = Path.Combine(Path.GetTempPath(), "equipment-size-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            byte[] bytes = [1, 2, 3]; File.WriteAllBytes(Path.Combine(root, "kiosk.exe"), bytes);
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            var kiosk = new SetupComponent("kiosk", "1.2.0", 4, hash);
            var manifest = new PayloadManifest(3, 3, "equipment-wpf", "1.5.0", "1.5.0", kiosk.Version, new string('a', 40), new string('0', 64), hash,
                "complete", 1, new("worker", "1.5.0", 1, new string('0', 64)), kiosk);
            File.WriteAllText(Path.Combine(root, "payload.json"), System.Text.Json.JsonSerializer.Serialize(manifest, Payload.Json)); Payload.UseDirectory(root);
            Assert.False(await Payload.Verify("kiosk.exe", hash, default));
        }
        finally { Payload.UseAssembly(typeof(MainWindow).Assembly); Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Mismatched_pinned_worker_version_stops_preparation()
    {
        string root = Path.Combine(Path.GetTempPath(), "equipment-version-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string work = Path.Combine(root, "work"); Directory.CreateDirectory(work);
        try
        {
            using var zip = Archive("extra.dll"); byte[] bytes = zip.ToArray(); File.WriteAllBytes(Path.Combine(root, "worker.zip"), bytes);
            var worker = new SetupComponent("worker", "1.5.0", bytes.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
            var kiosk = new SetupComponent("kiosk", "1.2.0", 1, new string('0', 64));
            var manifest = new PayloadManifest(3, 3, "equipment-wpf", "1.5.0", worker.Version, kiosk.Version, new string('a', 40), worker.Sha256, kiosk.Sha256,
                "complete", 1, worker, kiosk);
            File.WriteAllText(Path.Combine(root, "payload.json"), System.Text.Json.JsonSerializer.Serialize(manifest, Payload.Json)); Payload.UseDirectory(root);
            using var http = new System.Net.Http.HttpClient(new NoNetwork());
            var error = await Assert.ThrowsAsync<ComponentPreparationException>(() => Payload.Prepare(new(true, false, 0, [], false), work, http, _ => { }, default));
            Assert.Contains("trabajador WinGet", error.Message); Assert.False(File.Exists(Path.Combine(work, "kiosk.exe")));
        }
        finally { Payload.UseAssembly(typeof(MainWindow).Assembly); Directory.Delete(root, true); }
    }
}
