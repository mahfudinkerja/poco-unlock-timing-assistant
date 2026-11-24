using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using PocoUnlockAssistant.Core.Abstractions;
using PocoUnlockAssistant.Core.Automation;
using PocoUnlockAssistant.Core.Persistence;

namespace PocoUnlockAssistant.Infrastructure.Persistence;

/// <summary>
/// SQLite audit trail. Stores one row per attempt plus the timestamps and evidence
/// required by the specification. It never stores Xiaomi credentials or tokens.
/// </summary>
public sealed class SqliteAttemptStore : IAttemptStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteAttemptStore(string databasePath)
    {
        _databasePath = databasePath;
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
    }

    public string DatabasePath => _databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS attempts (
                attempt_id              TEXT PRIMARY KEY,
                scheduled_beijing       TEXT NOT NULL,
                scheduled_jakarta       TEXT NOT NULL,
                ntp_server              TEXT NULL,
                ntp_offset_ms           REAL NULL,
                time_uncertainty_ms     REAL NULL,
                network_latency_ms      REAL NULL,
                device_model            TEXT NULL,
                hyperos_version         TEXT NULL,
                adb_status              TEXT NULL,
                community_version       TEXT NULL,
                mode                    TEXT NOT NULL,
                ui_verification         TEXT NULL,
                triggered_at            TEXT NULL,
                adb_completed_at        TEXT NULL,
                response_observed_at    TEXT NULL,
                result                  TEXT NULL,
                confidence              TEXT NULL,
                state                   TEXT NOT NULL,
                error_message           TEXT NULL,
                screenshot_path         TEXT NULL,
                created_at              TEXT NOT NULL,
                updated_at              TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_attempts_scheduled ON attempts(scheduled_beijing);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(AttemptRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO attempts (
                attempt_id, scheduled_beijing, scheduled_jakarta, ntp_server, ntp_offset_ms,
                time_uncertainty_ms, network_latency_ms, device_model, hyperos_version, adb_status,
                community_version, mode, ui_verification, triggered_at, adb_completed_at,
                response_observed_at, result, confidence, state, error_message, screenshot_path,
                created_at, updated_at)
            VALUES (
                $id, $sbj, $sjk, $ntp, $offset, $unc, $lat, $model, $hyper, $adb, $community, $mode,
                $verify, $triggered, $completed, $observed, $result, $confidence, $state, $error,
                $screenshot, $created, $updated)
            ON CONFLICT(attempt_id) DO UPDATE SET
                ntp_server = excluded.ntp_server,
                ntp_offset_ms = excluded.ntp_offset_ms,
                time_uncertainty_ms = excluded.time_uncertainty_ms,
                network_latency_ms = excluded.network_latency_ms,
                device_model = excluded.device_model,
                hyperos_version = excluded.hyperos_version,
                adb_status = excluded.adb_status,
                community_version = excluded.community_version,
                mode = excluded.mode,
                ui_verification = excluded.ui_verification,
                triggered_at = excluded.triggered_at,
                adb_completed_at = excluded.adb_completed_at,
                response_observed_at = excluded.response_observed_at,
                result = excluded.result,
                confidence = excluded.confidence,
                state = excluded.state,
                error_message = excluded.error_message,
                screenshot_path = excluded.screenshot_path,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue("$id", record.AttemptId.ToString());
        command.Parameters.AddWithValue("$sbj", Iso(record.ScheduledBeijing));
        command.Parameters.AddWithValue("$sjk", Iso(record.ScheduledJakarta));
        command.Parameters.AddWithValue("$ntp", (object?)record.NtpServer ?? DBNull.Value);
        command.Parameters.AddWithValue("$offset", (object?)record.NtpOffsetMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$unc", (object?)record.TimeUncertaintyMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$lat", (object?)record.NetworkLatencyMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$model", (object?)record.DeviceModel ?? DBNull.Value);
        command.Parameters.AddWithValue("$hyper", (object?)record.HyperOsVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$adb", (object?)record.AdbStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$community", (object?)record.CommunityVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$mode", record.Mode.ToString());
        command.Parameters.AddWithValue("$verify", (object?)record.UiVerification ?? DBNull.Value);
        command.Parameters.AddWithValue("$triggered", (object?)NullableIso(record.TriggeredAtUtc) ?? DBNull.Value);
        command.Parameters.AddWithValue("$completed", (object?)NullableIso(record.AdbCompletedAtUtc) ?? DBNull.Value);
        command.Parameters.AddWithValue("$observed", (object?)NullableIso(record.ResponseObservedAtUtc) ?? DBNull.Value);
        command.Parameters.AddWithValue("$result", (object?)record.Result?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$confidence", (object?)record.Confidence?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$state", record.State.ToString());
        command.Parameters.AddWithValue("$error", (object?)record.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$screenshot", (object?)record.ScreenshotPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", Iso(record.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated", Iso(record.UpdatedAtUtc));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AttemptRecord>> GetRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM attempts ORDER BY scheduled_beijing DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));

        var records = new List<AttemptRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(Read(reader));
        }

        return records;
    }

    public async Task<AttemptRecord?> FindByScheduledAsync(
        DateTimeOffset scheduledBeijing,
        CancellationToken cancellationToken = default)
    {
        var recent = await GetRecentAsync(200, cancellationToken).ConfigureAwait(false);
        var key = scheduledBeijing.ToUniversalTime();
        return recent.FirstOrDefault(r => r.ScheduledBeijing.ToUniversalTime() == key);
    }

    /// <summary>Duplicate-execution protection: an attempt that already triggered may not run again.</summary>
    public async Task<bool> HasExecutedAtAsync(
        DateTimeOffset scheduledBeijing,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindByScheduledAsync(scheduledBeijing, cancellationToken).ConfigureAwait(false);
        return existing is { TriggeredAtUtc: not null };
    }

    public async Task<string> ExportCsvAsync(CancellationToken cancellationToken = default)
    {
        var records = await GetRecentAsync(1000, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var path = Path.Combine(Path.GetDirectoryName(_databasePath)!, "attempts.csv");

        var builder = new StringBuilder();
        builder.AppendLine(
            "attempt_id,scheduled_beijing,scheduled_jakarta,ntp_server,ntp_offset_ms,time_uncertainty_ms," +
            "network_latency_ms,device_model,hyperos_version,adb_status,community_version,mode," +
            "ui_verification,triggered_at,adb_completed_at,response_observed_at,result,confidence,state," +
            "error_message,screenshot_path");

        foreach (var r in records)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                Csv(r.AttemptId.ToString()), Csv(Iso(r.ScheduledBeijing)), Csv(Iso(r.ScheduledJakarta)),
                Csv(r.NtpServer), Csv(r.NtpOffsetMs?.ToString("0.##")),
                Csv(r.TimeUncertaintyMs?.ToString("0.##")), Csv(r.NetworkLatencyMs?.ToString("0.##")),
                Csv(r.DeviceModel), Csv(r.HyperOsVersion), Csv(r.AdbStatus), Csv(r.CommunityVersion),
                Csv(r.Mode.ToString()), Csv(r.UiVerification), Csv(NullableIso(r.TriggeredAtUtc)),
                Csv(NullableIso(r.AdbCompletedAtUtc)), Csv(NullableIso(r.ResponseObservedAtUtc)),
                Csv(r.Result?.ToString()), Csv(r.Confidence?.ToString()), Csv(r.State.ToString()),
                Csv(r.ErrorMessage), Csv(r.ScreenshotPath),
            }));
        }

        await File.WriteAllTextAsync(path, builder.ToString(), cancellationToken).ConfigureAwait(false);
        return path;
    }

    public async Task<string> ExportJsonAsync(CancellationToken cancellationToken = default)
    {
        var records = await GetRecentAsync(1000, cancellationToken).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(_databasePath)!;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "attempts.json");
        var json = JsonSerializer.Serialize(records, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static AttemptRecord Read(SqliteDataReader reader)
    {
        string? GetString(string column)
        {
            var index = reader.GetOrdinal(column);
            return reader.IsDBNull(index) ? null : reader.GetString(index);
        }

        double? GetDouble(string column)
        {
            var index = reader.GetOrdinal(column);
            return reader.IsDBNull(index) ? null : reader.GetDouble(index);
        }

        DateTimeOffset? GetDate(string column) =>
            DateTimeOffset.TryParse(GetString(column), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var value)
                ? value
                : null;

        return new AttemptRecord
        {
            AttemptId = Guid.Parse(GetString("attempt_id")!),
            ScheduledBeijing = GetDate("scheduled_beijing") ?? DateTimeOffset.MinValue,
            ScheduledJakarta = GetDate("scheduled_jakarta") ?? DateTimeOffset.MinValue,
            NtpServer = GetString("ntp_server"),
            NtpOffsetMs = GetDouble("ntp_offset_ms"),
            TimeUncertaintyMs = GetDouble("time_uncertainty_ms"),
            NetworkLatencyMs = GetDouble("network_latency_ms"),
            DeviceModel = GetString("device_model"),
            HyperOsVersion = GetString("hyperos_version"),
            AdbStatus = GetString("adb_status"),
            CommunityVersion = GetString("community_version"),
            Mode = Enum.TryParse<ExecutionMode>(GetString("mode"), out var mode) ? mode : ExecutionMode.ManualAssisted,
            UiVerification = GetString("ui_verification"),
            TriggeredAtUtc = GetDate("triggered_at"),
            AdbCompletedAtUtc = GetDate("adb_completed_at"),
            ResponseObservedAtUtc = GetDate("response_observed_at"),
            Result = Enum.TryParse<FinalResult>(GetString("result"), out var result) ? result : null,
            Confidence = Enum.TryParse<ConfidenceLevel>(GetString("confidence"), out var confidence) ? confidence : null,
            State = Enum.TryParse<SubmissionState>(GetString("state"), out var state) ? state : SubmissionState.Idle,
            ErrorMessage = GetString("error_message"),
            ScreenshotPath = GetString("screenshot_path"),
            CreatedAtUtc = GetDate("created_at") ?? DateTimeOffset.MinValue,
            UpdatedAtUtc = GetDate("updated_at") ?? DateTimeOffset.MinValue,
        };
    }

    private static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? NullableIso(DateTimeOffset? value) => value is null ? null : Iso(value.Value);

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
