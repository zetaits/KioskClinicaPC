using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

if (args.Length == 3 && args[0] == "sign")
{
    string privatePem = Environment.GetEnvironmentVariable("KIOSK_UPDATE_SIGNING_PRIVATE_KEY") ?? "";
    string publicPem = Environment.GetEnvironmentVariable("KIOSK_UPDATE_SIGNING_PUBLIC_KEY") ?? "";
    if (string.IsNullOrWhiteSpace(privatePem) || string.IsNullOrWhiteSpace(publicPem))
    {
        Console.Error.WriteLine("Faltan las claves ECDSA de firma en el entorno.");
        return 2;
    }

    byte[] manifest = await File.ReadAllBytesAsync(args[1]);
    using var signer = ECDsa.Create();
    using var verifier = ECDsa.Create();
    signer.ImportFromPem(privatePem);
    verifier.ImportFromPem(publicPem);
    byte[] signature = signer.SignData(manifest, HashAlgorithmName.SHA256);
    if (!verifier.VerifyData(manifest, signature, HashAlgorithmName.SHA256))
        throw new CryptographicException("La clave pública configurada no corresponde con la privada.");
    await File.WriteAllTextAsync(args[2], Convert.ToBase64String(signature), Encoding.ASCII);
    return 0;
}

if (args.Length == 4 && args[0] is "protect" or "unprotect")
{
    string version = args[1];
    if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"))
        throw new ArgumentException("Version debe tener formato X.Y.Z.");
    string secret = Environment.GetEnvironmentVariable("KIOSK_RELEASE_PUBLISH_KEY") ?? "";
    if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("Falta KIOSK_RELEASE_PUBLISH_KEY.");
    byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes("kiosk-release-artifact-v1:" + secret));
    string[] names =
    [
        $"Setup-KioskClinicaPC-{version}.exe",
        $"Setup-KioskClinicaPC-{version}.exe.sha256",
        $"kiosk-release-{version}.json",
        $"kiosk-release-{version}.json.sig"
    ];
    byte[] magic = Encoding.ASCII.GetBytes("KIOSKREL1");
    const int nonceLength = 12, tagLength = 16;

    if (args[0] == "protect")
    {
        using var plain = new MemoryStream();
        using (var archive = new ZipArchive(plain, ZipArchiveMode.Create, true))
        {
            foreach (string name in names)
            {
                string file = Path.Combine(args[2], name);
                using var source = File.OpenRead(file);
                using var target = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
                await source.CopyToAsync(target);
            }
        }
        byte[] nonce = RandomNumberGenerator.GetBytes(nonceLength);
        byte[] tag = new byte[tagLength];
        byte[] cipher = new byte[checked((int)plain.Length)];
        using (var aes = new AesGcm(key, tagLength))
            aes.Encrypt(nonce, plain.GetBuffer().AsSpan(0, (int)plain.Length), cipher, tag);
        await using var output = File.Create(args[3]);
        await output.WriteAsync(magic);
        await output.WriteAsync(nonce);
        await output.WriteAsync(tag);
        await output.WriteAsync(cipher);
    }
    else
    {
        byte[] data = await File.ReadAllBytesAsync(args[2]);
        int offset = magic.Length + nonceLength + tagLength;
        if (data.Length <= offset || !data.AsSpan(0, magic.Length).SequenceEqual(magic))
            throw new InvalidDataException("Formato de artefacto no válido.");
        byte[] plain = new byte[data.Length - offset];
        using (var aes = new AesGcm(key, tagLength))
            aes.Decrypt(data.AsSpan(magic.Length, nonceLength), data.AsSpan(offset),
                data.AsSpan(magic.Length + nonceLength, tagLength), plain);
        using var archive = new ZipArchive(new MemoryStream(plain), ZipArchiveMode.Read);
        if (archive.Entries.Count != names.Length ||
            !archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal)
                .SequenceEqual(names.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("El artefacto no contiene los cuatro archivos esperados.");
        Directory.CreateDirectory(args[3]);
        foreach (var entry in archive.Entries)
        {
            string destination = Path.Combine(args[3], entry.FullName);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            await using var source = entry.Open();
            await source.CopyToAsync(output);
        }
    }
    return 0;
}

Console.Error.WriteLine("Uso: sign <manifest> <signature> | protect <version> <dir> <encrypted> | unprotect <version> <encrypted> <dir>");
return 2;
