using Microsoft.Data.Sqlite;
using NextIntegrations.Devices.Abstractions;

namespace NextIntegrations.Devices.Registry;

/// <summary>
/// Database-backed device registry (the device "baza"): a shared SQLite DB
/// (<c>LocalApplicationData/Horeca/devices.db</c>) holding every configured device integration. Reads
/// hit the DB live, so a device added in one app is immediately visible to the others. On first run it
/// imports any legacy <c>devices.json</c> so existing configuration carries over.
/// </summary>
public sealed class SqliteDeviceRegistry : IDeviceRegistry
{
    private readonly string _connectionString;
    private readonly object _gate = new();

    private SqliteDeviceRegistry(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    /// <summary>Opens (creating + migrating) the shared device DB at <paramref name="dbPath"/> (default shared path).</summary>
    public static SqliteDeviceRegistry Load(string? dbPath = null)
    {
        var registry = new SqliteDeviceRegistry(dbPath ?? DefaultDbPath());
        registry.EnsureSchema();
        registry.ImportLegacyJsonIfEmpty(dbPath is null);
        return registry;
    }

    public bool SimulateMode
    {
        get
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM Settings WHERE Key = 'SimulateMode' LIMIT 1;";
            object? value = command.ExecuteScalar();
            return value is null || string.Equals(value.ToString(), "1", StringComparison.Ordinal);
        }
    }

    public void SetSimulateMode(bool value)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO Settings(Key, Value) VALUES('SimulateMode', $v) " +
                "ON CONFLICT(Key) DO UPDATE SET Value = $v;";
            command.Parameters.AddWithValue("$v", value ? "1" : "0");
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<DeviceConfig> GetAll()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " FROM Devices ORDER BY Name;";
        using SqliteDataReader reader = command.ExecuteReader();
        var devices = new List<DeviceConfig>();
        while (reader.Read())
        {
            devices.Add(Map(reader));
        }

        return devices;
    }

    public DeviceConfig? GetForRole(DeviceRole role)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " FROM Devices WHERE Role = $role AND Enabled = 1 ORDER BY Name LIMIT 1;";
        command.Parameters.AddWithValue("$role", (int)role);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void AddOrUpdate(DeviceConfig device)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO Devices(Id, Role, Protocol, Connection, Name, Enabled, Host, Port, SerialPort, BaudRate, Address, CharactersPerLine)
                VALUES($id, $role, $protocol, $connection, $name, $enabled, $host, $port, $serialPort, $baudRate, $address, $cpl)
                ON CONFLICT(Id) DO UPDATE SET
                    Role = $role, Protocol = $protocol, Connection = $connection, Name = $name, Enabled = $enabled,
                    Host = $host, Port = $port, SerialPort = $serialPort, BaudRate = $baudRate, Address = $address,
                    CharactersPerLine = $cpl;
                """;
            BindDevice(command, device);
            command.ExecuteNonQuery();
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Devices WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    public void ReplaceAll(IReadOnlyList<DeviceConfig> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        lock (_gate)
        {
            using var connection = Open();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using (var clear = connection.CreateCommand())
            {
                clear.Transaction = transaction;
                clear.CommandText = "DELETE FROM Devices;";
                clear.ExecuteNonQuery();
            }

            foreach (DeviceConfig device in devices)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    "INSERT INTO Devices(Id, Role, Protocol, Connection, Name, Enabled, Host, Port, SerialPort, BaudRate, Address, CharactersPerLine) " +
                    "VALUES($id, $role, $protocol, $connection, $name, $enabled, $host, $port, $serialPort, $baudRate, $address, $cpl);";
                BindDevice(insert, device);
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    /// <summary>The shared device DB: <c>LocalApplicationData/Horeca/devices.db</c>.</summary>
    public static string DefaultDbPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Horeca",
        "devices.db");

    private const string SelectColumns =
        "SELECT Id, Role, Protocol, Connection, Name, Enabled, Host, Port, SerialPort, BaudRate, Address, CharactersPerLine";

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void EnsureSchema()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS Devices(
                    Id TEXT PRIMARY KEY,
                    Role INTEGER NOT NULL,
                    Protocol INTEGER NOT NULL,
                    Connection INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    Enabled INTEGER NOT NULL,
                    Host TEXT,
                    Port INTEGER NOT NULL,
                    SerialPort TEXT,
                    BaudRate INTEGER NOT NULL,
                    Address TEXT,
                    CharactersPerLine INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS Settings(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
                """;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>One-time bootstrap: if the DB has no devices yet, import the legacy shared devices.json.</summary>
    private void ImportLegacyJsonIfEmpty(bool useDefaultJsonPath)
    {
        try
        {
            using (var connection = Open())
            using (var count = connection.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM Devices;";
                if (Convert.ToInt64(count.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0)
                {
                    return;
                }
            }

            string jsonPath = JsonDeviceRegistry.DefaultFilePath();
            if (!useDefaultJsonPath || !File.Exists(jsonPath))
            {
                return;
            }

            JsonDeviceRegistry legacy = JsonDeviceRegistry.Load(jsonPath);
            IReadOnlyList<DeviceConfig> devices = legacy.GetAll();
            if (devices.Count > 0)
            {
                ReplaceAll(devices);
            }

            SetSimulateMode(legacy.SimulateMode);
        }
#pragma warning disable CA1031 // Import is best-effort; a fresh DB is fine if it fails.
        catch (Exception)
#pragma warning restore CA1031
        {
            // ignore
        }
    }

    private static void BindDevice(SqliteCommand command, DeviceConfig device)
    {
        command.Parameters.AddWithValue("$id", device.Id);
        command.Parameters.AddWithValue("$role", (int)device.Role);
        command.Parameters.AddWithValue("$protocol", (int)device.Protocol);
        command.Parameters.AddWithValue("$connection", (int)device.Connection);
        command.Parameters.AddWithValue("$name", device.Name);
        command.Parameters.AddWithValue("$enabled", device.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$host", (object?)device.Host ?? DBNull.Value);
        command.Parameters.AddWithValue("$port", device.Port);
        command.Parameters.AddWithValue("$serialPort", (object?)device.SerialPort ?? DBNull.Value);
        command.Parameters.AddWithValue("$baudRate", device.BaudRate);
        command.Parameters.AddWithValue("$address", (object?)device.Address ?? DBNull.Value);
        command.Parameters.AddWithValue("$cpl", device.CharactersPerLine);
    }

    private static DeviceConfig Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Role = (DeviceRole)reader.GetInt32(1),
        Protocol = (DeviceProtocol)reader.GetInt32(2),
        Connection = (DeviceConnectionKind)reader.GetInt32(3),
        Name = reader.GetString(4),
        Enabled = reader.GetInt32(5) != 0,
        Host = reader.IsDBNull(6) ? null : reader.GetString(6),
        Port = reader.GetInt32(7),
        SerialPort = reader.IsDBNull(8) ? null : reader.GetString(8),
        BaudRate = reader.GetInt32(9),
        Address = reader.IsDBNull(10) ? null : reader.GetString(10),
        CharactersPerLine = reader.GetInt32(11)
    };
}
