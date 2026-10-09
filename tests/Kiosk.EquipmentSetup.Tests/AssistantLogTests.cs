using System.IO;
using System.Text.Json;
using Kiosk.EquipmentSetup;
using Xunit;

namespace Kiosk.EquipmentSetup.Tests;

public sealed class AssistantLogTests
{
    [Fact]
    public void Frontend_errors_are_recorded_without_raw_messages_or_credentials()
    {
        string directory = Path.Combine(Path.GetTempPath(), "assistant-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var error = new IOException("untrusted-key-and-password", new JsonException("untrusted-verifier"));
            string path = AssistantLog.Write("resultado del trabajador", error, directory)!;
            string log = File.ReadAllText(path);
            Assert.Contains("resultado del trabajador", log);
            Assert.Contains("IOException", log); Assert.Contains("JsonException", log);
            Assert.DoesNotContain("untrusted", log);
            string obstructed = Path.Combine(directory, "blocked"); File.WriteAllText(obstructed, "file");
            Assert.Null(AssistantLog.Write("resultado del trabajador", error, obstructed));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
