using System.Security.Cryptography;

if (args.Length != 3 || args[0] != "sign")
{
    Console.Error.WriteLine("Uso: Kiosk.ReleaseTool sign <manifest> <signature>");
    return 2;
}

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
await File.WriteAllTextAsync(args[2], Convert.ToBase64String(signature), System.Text.Encoding.ASCII);
return 0;
