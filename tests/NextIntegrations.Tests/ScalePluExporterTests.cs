using System.Text;
using NextIntegrations.Devices.Scale;
using Xunit;

namespace NextIntegrations.Tests;

/// <summary>The shared scale PLU CSV exporter (CAS CL-Works / DIGI watched-folder format).</summary>
public sealed class ScalePluExporterTests
{
    [Fact]
    public async Task WritesHeaderedCsv_WithOneRowPerPlu()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ni-scale-{Guid.NewGuid():N}.csv");
        try
        {
            int sent = await ScalePluExporter.WriteCsvAsync(path,
            [
                new ScalePluRow(7, "Alma", 2.49m, ByWeight: true),
                new ScalePluRow(8, "Su", 0.60m, ByWeight: false),
            ]);

            Assert.Equal(2, sent);
            string content = await File.ReadAllTextAsync(path, Encoding.UTF8);
            string[] lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("PLU;Ad;Qiymet;Vahid", lines[0]);
            Assert.Equal("7;Alma;2.49;kq", lines[1]);
            Assert.Equal("8;Su;0.60;ədəd", lines[2]);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task SanitisesSemicolonsInNames_AndCreatesMissingDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ni-scale-dir-{Guid.NewGuid():N}");
        string path = Path.Combine(dir, "nested", "plu.csv");
        try
        {
            await ScalePluExporter.WriteCsvAsync(path, [new ScalePluRow(1, "Alma; Qirmizi", 1.00m, ByWeight: true)]);

            Assert.True(File.Exists(path));
            string content = await File.ReadAllTextAsync(path, Encoding.UTF8);
            Assert.Contains("Alma, Qirmizi", content, StringComparison.Ordinal); // ';' → ','
            Assert.Equal(4, content.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1].Split(';').Length);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
