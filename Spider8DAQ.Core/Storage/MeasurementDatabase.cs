using Microsoft.Data.Sqlite;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Storage;

public sealed class MeasurementRecord
{
    public long Id { get; set; }
    public DateTime CreatedLocal { get; set; }
    public string ProjectName { get; set; } = "";
    public string Operator { get; set; } = "";
    public string SampleId { get; set; } = "";
    public string Comment { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Tags { get; set; } = "";
    public int SampleCount { get; set; }
    public string Backend { get; set; } = "";
}

public sealed class MeasurementDatabase : IAsyncDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _conn;

    public MeasurementDatabase(string dbPath)
    {
        _dbPath = dbPath;
    }

    public async Task InitializeAsync()
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(_dbPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _conn = new SqliteConnection($"Data Source={_dbPath}");
        await _conn.OpenAsync();
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS measurements (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              created_local TEXT NOT NULL,
              project_name TEXT NOT NULL,
              operator TEXT NOT NULL,
              sample_id TEXT NOT NULL,
              comment TEXT NOT NULL,
              file_path TEXT NOT NULL,
              tags TEXT NOT NULL,
              sample_count INTEGER NOT NULL,
              backend TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_meas_created ON measurements(created_local);
            CREATE INDEX IF NOT EXISTS ix_meas_sample ON measurements(sample_id);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<long> InsertAsync(MeasurementRecord record, CancellationToken ct = default)
    {
        Ensure();
        await using var cmd = _conn!.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO measurements(created_local, project_name, operator, sample_id, comment, file_path, tags, sample_count, backend)
            VALUES ($c,$p,$o,$s,$m,$f,$t,$n,$b);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$c", record.CreatedLocal.ToString("O"));
        cmd.Parameters.AddWithValue("$p", record.ProjectName);
        cmd.Parameters.AddWithValue("$o", record.Operator);
        cmd.Parameters.AddWithValue("$s", record.SampleId);
        cmd.Parameters.AddWithValue("$m", record.Comment);
        cmd.Parameters.AddWithValue("$f", record.FilePath);
        cmd.Parameters.AddWithValue("$t", record.Tags);
        cmd.Parameters.AddWithValue("$n", record.SampleCount);
        cmd.Parameters.AddWithValue("$b", record.Backend);
        var id = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
        return id;
    }

    public async Task<IReadOnlyList<MeasurementRecord>> SearchAsync(string? query, int limit = 200, CancellationToken ct = default)
    {
        Ensure();
        await using var cmd = _conn!.CreateCommand();
        if (string.IsNullOrWhiteSpace(query))
        {
            cmd.CommandText = "SELECT * FROM measurements ORDER BY id DESC LIMIT $lim;";
            cmd.Parameters.AddWithValue("$lim", limit);
        }
        else
        {
            cmd.CommandText =
                """
                SELECT * FROM measurements
                WHERE project_name LIKE $q OR operator LIKE $q OR sample_id LIKE $q OR comment LIKE $q OR tags LIKE $q OR file_path LIKE $q
                ORDER BY id DESC LIMIT $lim;
                """;
            cmd.Parameters.AddWithValue("$q", "%" + query.Trim() + "%");
            cmd.Parameters.AddWithValue("$lim", limit);
        }

        var list = new List<MeasurementRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new MeasurementRecord
            {
                Id = reader.GetInt64(0),
                CreatedLocal = DateTime.TryParse(reader.GetString(1), out var dt) ? dt : DateTime.MinValue,
                ProjectName = reader.GetString(2),
                Operator = reader.GetString(3),
                SampleId = reader.GetString(4),
                Comment = reader.GetString(5),
                FilePath = reader.GetString(6),
                Tags = reader.GetString(7),
                SampleCount = reader.GetInt32(8),
                Backend = reader.GetString(9)
            });
        }
        return list;
    }

    public static MeasurementRecord FromSession(
        string filePath,
        ProjectMeta meta,
        string projectName,
        string backend,
        int sampleCount,
        string tags = "") => new()
    {
        CreatedLocal = DateTime.Now,
        ProjectName = projectName,
        Operator = meta.Operator,
        SampleId = meta.SampleId,
        Comment = meta.Comment,
        FilePath = filePath,
        Tags = tags,
        SampleCount = sampleCount,
        Backend = backend
    };

    private void Ensure()
    {
        if (_conn is null) throw new InvalidOperationException("Database not initialized.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_conn is not null)
        {
            await _conn.DisposeAsync();
            _conn = null;
        }
    }
}
