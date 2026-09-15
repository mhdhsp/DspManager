using System.Text;
using HotelConfigAnalyser.Models;
using HotelConfigAnalyser.Schema;
using HotelConfigAnalyser.Utilities;
using Microsoft.Extensions.Logging;

namespace HotelConfigAnalyser.Services;

/// <summary>
/// Generates a complete, valid MySQL INSERT script from a <see cref="NormalisedConfiguration"/>.
///
/// Rules enforced:
///   - AUTO_INCREMENT columns are never included.
///   - Only INSERT statements are generated (no DROP / TRUNCATE / DELETE / ALTER).
///   - All values pass through <see cref="MySqlLiteralFormatter"/> for safe escaping.
///   - Table names are resolved through <see cref="ConfigurationTableMapping"/>.
///   - The script may be wrapped in START TRANSACTION / COMMIT.
///   - SQL generation order: databases → settings_master → settings_details
///                           → params_master → params_settings
/// </summary>
public sealed class SqlGenerator
{
    private readonly ILogger<SqlGenerator> _logger;

    public SqlGenerator(ILogger<SqlGenerator> logger)
    {
        _logger = logger;
    }

    public string Generate(NormalisedConfiguration config)
    {
        _logger.LogInformation("SQL generation started. Environment={Env}, Port={Port}, Version={Version}",
            config.Environment, config.Port, config.Version);

        var sb = new StringBuilder(8192);

        WriteFileHeader(sb, config);

        sb.AppendLine();
        sb.AppendLine($"USE `{ConfigurationTableMapping.DatabaseName}`;");
        sb.AppendLine();

        if (config.IncludeTransaction)
        {
            sb.AppendLine("START TRANSACTION;");
            sb.AppendLine();
        }

        // ── 1. Databases ──────────────────────────────────────────────────
        GenerateDatabases(sb, config);

        // ── 2. Settings Master ────────────────────────────────────────────
        GenerateSettingsMaster(sb, config);

        // ── 3. Settings Details ───────────────────────────────────────────
        GenerateSettingsDetails(sb, config);

        // ── 4. Params Master ──────────────────────────────────────────────
        GenerateParamsMaster(sb, config);

        // ── 5. Params Settings ────────────────────────────────────────────
        GenerateParamsSettings(sb, config);

        if (config.IncludeTransaction)
        {
            sb.AppendLine();
            sb.AppendLine("COMMIT;");
        }

        var sql = sb.ToString();
        _logger.LogInformation("SQL generation completed. Output length: {Length} characters.", sql.Length);
        return sql;
    }

    // ── File header ───────────────────────────────────────────────────────

    private static void WriteFileHeader(StringBuilder sb, NormalisedConfiguration config)
    {
        sb.AppendLine("-- =====================================================");
        sb.AppendLine("-- Hotel Configuration Migration");
        sb.AppendLine($"-- Database:     {ConfigurationTableMapping.DatabaseName}");
        sb.AppendLine($"-- Environment:  {config.Environment}");
        sb.AppendLine($"-- Port:         {config.Port}");
        sb.AppendLine($"-- Version:      {config.Version}");
        sb.AppendLine($"-- Source file:  {config.FileName}");
        sb.AppendLine($"-- Generated:    {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("-- =====================================================");
        sb.AppendLine("--");
        sb.AppendLine("-- WARNING: This script may contain sensitive credentials.");
        sb.AppendLine("-- Store and transfer this file securely.");
        sb.AppendLine("--");
        sb.AppendLine("-- Phase 1 — INSERT only. No UPDATE / DELETE / DROP generated.");
        sb.AppendLine("-- =====================================================");
    }

    // ── Section header ────────────────────────────────────────────────────

    private static void WriteSectionHeader(StringBuilder sb, string tableName, int recordCount)
    {
        sb.AppendLine();
        sb.AppendLine("-- =====================================================");
        sb.AppendLine($"-- {tableName}");
        sb.AppendLine($"-- Records: {recordCount}");
        sb.AppendLine("-- =====================================================");
        sb.AppendLine();
    }

    // ── 1. Databases ──────────────────────────────────────────────────────

    private static void GenerateDatabases(StringBuilder sb, NormalisedConfiguration config)
    {
        var tableName = ConfigurationTableMapping.GetTableName(
            ConfigurationTableMapping.KeyDatabases, config.Environment);

        WriteSectionHeader(sb, tableName, config.Databases.Count);

        foreach (var db in config.Databases)
        {
            sb.AppendLine($"INSERT INTO `{tableName}`");
            sb.AppendLine("(");
            sb.AppendLine("    `DataBaseType`,");
            sb.AppendLine("    `Description`,");
            sb.AppendLine("    `UserName`,");
            sb.AppendLine("    `Password`,");
            sb.AppendLine("    `DataBaseName`,");
            sb.AppendLine("    `Server`,");
            sb.AppendLine("    `Provider`,");
            sb.AppendLine("    `ActiveStatus`,");
            sb.AppendLine("    `ReadEnable`,");
            sb.AppendLine("    `WriteEnable`,");
            sb.AppendLine("    `AUI`,");
            sb.AppendLine("    `RecordStatus`,");
            sb.AppendLine("    `Port`,");
            sb.AppendLine("    `Version`,");
            sb.AppendLine("    `Environment`,");
            sb.AppendLine("    `ActivePeriodBegin`,");
            sb.AppendLine("    `ActivePeriodEnd`");
            sb.AppendLine(")");
            sb.AppendLine("VALUES");
            sb.AppendLine("(");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatInt(db.DataBaseType)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(db.Description)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(db.UserName)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(db.Password)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(db.DataBaseName)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(db.Server)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(db.Provider)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatInt(db.ActiveStatus)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatInt(db.ReadEnable)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatInt(db.WriteEnable)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(db.Aui)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatInt(db.RecordStatus)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(db.Port)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(db.Version)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(db.Environment)},");
            sb.AppendLine($"    {(db.ActivePeriodBegin.HasValue ? MySqlLiteralFormatter.Format(db.ActivePeriodBegin.Value) : "NULL")},");
            sb.AppendLine($"    {(db.ActivePeriodEnd.HasValue ? MySqlLiteralFormatter.Format(db.ActivePeriodEnd.Value) : "NULL")}");
            sb.AppendLine(");");
            sb.AppendLine();
        }
    }

    // ── 2. Settings Master ────────────────────────────────────────────────

    private static void GenerateSettingsMaster(StringBuilder sb, NormalisedConfiguration config)
    {
        var tableName = ConfigurationTableMapping.GetTableName(
            ConfigurationTableMapping.KeySettingsMaster, config.Environment);

        WriteSectionHeader(sb, tableName, config.SettingsMaster.Count);

        foreach (var entry in config.SettingsMaster)
        {
            sb.AppendLine($"INSERT INTO `{tableName}`");
            sb.AppendLine("(");
            sb.AppendLine("    `SettingHead`,");
            sb.AppendLine("    `SettingsType`,");
            sb.AppendLine("    `RecordStatus`,");
            sb.AppendLine("    `Port`,");
            sb.AppendLine("    `Version`,");
            sb.AppendLine("    `Environment`");
            sb.AppendLine(")");
            sb.AppendLine("VALUES");
            sb.AppendLine("(");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.SettingHead)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.SettingsType)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatInt(entry.RecordStatus)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.Port)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.Version)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.Environment)}");
            sb.AppendLine(");");
            sb.AppendLine();
        }
    }

    // ── 3. Settings Details ───────────────────────────────────────────────

    private static void GenerateSettingsDetails(StringBuilder sb, NormalisedConfiguration config)
    {
        var tableName = ConfigurationTableMapping.GetTableName(
            ConfigurationTableMapping.KeySettingsDetails, config.Environment);

        WriteSectionHeader(sb, tableName, config.SettingsDetails.Count);

        foreach (var entry in config.SettingsDetails)
        {
            sb.AppendLine($"INSERT INTO `{tableName}`");
            sb.AppendLine("(");
            sb.AppendLine("    `MemberName`,");
            sb.AppendLine("    `MemberValue`,");
            sb.AppendLine("    `MemeberDescription`,");
            sb.AppendLine("    `MemberDataType`,");
            sb.AppendLine("    `SettingsHead`,");
            sb.AppendLine("    `RecordStatus`,");
            sb.AppendLine("    `AUI`,");
            sb.AppendLine("    `Port`,");
            sb.AppendLine("    `Version`,");
            sb.AppendLine("    `Environment`");
            sb.AppendLine(")");
            sb.AppendLine("VALUES");
            sb.AppendLine("(");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.MemberName)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(entry.MemberValue)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(entry.MemberDescription)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(entry.MemberDataType)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.SettingsHead)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatInt(entry.RecordStatus)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(entry.Aui)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.Port)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.Version)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.Environment)}");
            sb.AppendLine(");");
            sb.AppendLine();
        }
    }

    // ── 4. Params Master ──────────────────────────────────────────────────

    private static void GenerateParamsMaster(StringBuilder sb, NormalisedConfiguration config)
    {
        var tableName = ConfigurationTableMapping.GetTableName(
            ConfigurationTableMapping.KeyParamsMaster, config.Environment);

        WriteSectionHeader(sb, tableName, config.ParamsMaster.Count);

        foreach (var entry in config.ParamsMaster)
        {
            sb.AppendLine($"INSERT INTO `{tableName}`");
            sb.AppendLine("(");
            sb.AppendLine("    `ParamsHead`,");
            sb.AppendLine("    `SettingHead`,");
            sb.AppendLine("    `RecordStatus`,");
            sb.AppendLine("    `Port`,");
            sb.AppendLine("    `Version`,");
            sb.AppendLine("    `Environment`");
            sb.AppendLine(")");
            sb.AppendLine("VALUES");
            sb.AppendLine("(");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.ParamsHead)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.SettingHead)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatInt(entry.RecordStatus)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.Port)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.Version)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.Environment)}");
            sb.AppendLine(");");
            sb.AppendLine();
        }
    }

    // ── 5. Params Settings ────────────────────────────────────────────────

    private static void GenerateParamsSettings(StringBuilder sb, NormalisedConfiguration config)
    {
        var tableName = ConfigurationTableMapping.GetTableName(
            ConfigurationTableMapping.KeyParamsSettings, config.Environment);

        WriteSectionHeader(sb, tableName, config.ParamsSettings.Count);

        foreach (var entry in config.ParamsSettings)
        {
            sb.AppendLine($"INSERT INTO `{tableName}`");
            sb.AppendLine("(");
            sb.AppendLine("    `MemberName`,");
            sb.AppendLine("    `MemberValue`,");
            sb.AppendLine("    `MemeberDescription`,");
            sb.AppendLine("    `MemberDataType`,");
            sb.AppendLine("    `ParamsHead`,");
            sb.AppendLine("    `RecordStatus`,");
            sb.AppendLine("    `AUI`,");
            sb.AppendLine("    `Port`,");
            sb.AppendLine("    `Version`,");
            sb.AppendLine("    `Environment`,");
            sb.AppendLine("    `ParentHead`");
            sb.AppendLine(")");
            sb.AppendLine("VALUES");
            sb.AppendLine("(");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.MemberName)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(entry.MemberValue)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(entry.MemberDescription)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(entry.MemberDataType)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.ParamsHead)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatInt(entry.RecordStatus)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(entry.Aui)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.Port)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.Version)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.Format(entry.Environment)},");
            sb.AppendLine($"    {MySqlLiteralFormatter.FormatNullableString(entry.ParentHead)}");
            sb.AppendLine(");");
            sb.AppendLine();
        }
    }
}
