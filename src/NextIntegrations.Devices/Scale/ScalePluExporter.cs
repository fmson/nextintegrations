using System.Globalization;
using System.Text;

namespace NextIntegrations.Devices.Scale;

/// <summary>One PLU row for a weighing scale. App-neutral — the price is in major currency units (e.g. 2.49).</summary>
public sealed record ScalePluRow(int Plu, string Name, decimal Price, bool ByWeight);

/// <summary>
/// Exports a PLU set to the CSV file the scale software (CAS CL-Works / DIGI) imports — the proven legacy
/// "watched folder" pipeline. Columns: <c>PLU;Ad;Qiymet;Vahid</c>, UTF-8 with BOM so Azerbaijani names
/// import correctly. App-neutral: each POS head maps its own catalog onto <see cref="ScalePluRow"/>.
/// </summary>
public static class ScalePluExporter
{
    /// <summary>Writes <paramref name="rows"/> as the scale-import CSV to <paramref name="path"/>; returns the row count.</summary>
    public static async Task<int> WriteCsvAsync(
        string path, IReadOnlyList<ScalePluRow> rows, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(rows);

        StringBuilder sb = new();
        sb.AppendLine("PLU;Ad;Qiymet;Vahid");
        foreach (ScalePluRow r in rows)
        {
            string price = r.Price.ToString("0.00", CultureInfo.InvariantCulture);
            string name = r.Name.Replace(';', ',').Trim(); // keep the columns aligned
            sb.Append(r.Plu.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(name).Append(';')
              .Append(price).Append(';')
              .Append(r.ByWeight ? "kq" : "ədəd").Append('\n');
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken)
            .ConfigureAwait(false);
        return rows.Count;
    }
}
