# Hotel Configuration Settings — JSON → Table Mapping

## Overview

This document describes how an uploaded hotel configuration JSON file is parsed,
normalized into an internal model, and then mapped to the target MySQL database tables.

This mapping is the authoritative reference for:
- The JSON parser
- The configuration mapper
- The SQL generator
- Future DB diff / migration engine

---

## Database

**Database name:** `wccorpcanadaprdmasdb`
(Configured centrally in `ConfigurationTableMapping.cs` — never hard-coded in services.)

---

## Target Table Groups

| Logical Name        | BETA table                  | LIVE table                  |
|---------------------|-----------------------------|-----------------------------|
| databases           | `set_htl_databases`         | `live_htl_databases`        |
| settings_master     | `set_htl_settings_master`   | `live_htl_settings_master`  |
| settings_details    | `set_htl_settings_details`  | `live_htl_settings_details` |
| params_master       | `set_htl_params_master`     | `live_htl_params_master`    |
| params_settings     | `set_htl_params_settings`   | `live_htl_params_settings`  |

Table prefix is resolved at runtime from the selected **Environment** (BETA or LIVE).

---

## JSON Structure Assumptions

The hotel configuration JSON is expected to follow one of these general patterns:

### Pattern A — Flat keyed sections

```json
{
  "Hotel": {
    "Region": "CA",
    "GDSswitch": "0",
    "MobileGDS": "AM"
  },
  "Email": {
    "SMTPServer": "smtp.example.com",
    "SMTPPort": "587"
  },
  "HtlGeneral": {
    "ChannelCodes": "WEB,GDS",
    "Decimals": "2",
    "HomeCurrencyCode": "CAD"
  },
  "Databases": [
    {
      "DataBaseType": 1,
      "Description": "Main DB",
      "UserName": "dbuser",
      "Password": "secret",
      "DataBaseName": "hoteldb",
      "Server": "db.example.com",
      "Provider": "MySql",
      "ActiveStatus": 1,
      "ReadEnable": 1,
      "WriteEnable": 1
    }
  ]
}
```

### Pattern B — Nested with ConnectionStrings / AppSettings style

```json
{
  "ConnectionStrings": { ... },
  "AppSettings": {
    "Hotel": { ... },
    "HtlGeneral": { ... }
  }
}
```

The parser handles both patterns. Unknown top-level keys are treated as configuration
sections and mapped to `settings_details` / `settings_master` unless they match the
special `Databases` / `ConnectionStrings` keys.

---

## Section Classification Rules

| JSON Top-Level Key            | Classification       | Target Tables                          |
|-------------------------------|----------------------|----------------------------------------|
| `Databases` (array)           | Database configs     | `*_htl_databases`                      |
| `ConnectionStrings`           | Database configs     | `*_htl_databases`                      |
| Keys matching params prefixes | Parameter sections   | `*_htl_params_master` + `*_params_settings` |
| All other object keys         | Settings sections    | `*_htl_settings_master` + `*_settings_details` |

**Params prefixes** (these section names are treated as parameter groups):
- `HtlGeneral`
- `HtlParams`
- `Params`
- Any key whose name starts with `Htl` and contains nested objects with multiple leaf values

If ambiguous, the parser classifies as a **settings section** and logs a warning.

---

## Normalized Internal Models

### SettingEntry

Represents one row in `*_htl_settings_details`.

| Field               | Source                          | Target Column        |
|---------------------|---------------------------------|----------------------|
| `SettingsHead`      | JSON parent key (e.g. `Hotel`)  | `SettingsHead`       |
| `MemberName`        | JSON child key (e.g. `Region`)  | `MemberName`         |
| `MemberValue`       | JSON child value (e.g. `CA`)    | `MemberValue`        |
| `MemberDescription` | Optional metadata / null        | `MemeberDescription` |
| `MemberDataType`    | Inferred from value type        | `MemberDataType`     |
| `RecordStatus`      | Always `1`                      | `RecordStatus`       |
| `AUI`               | Optional metadata / null        | `AUI`                |
| `Port`              | User input                      | `Port`               |
| `Version`           | User input                      | `Version`            |
| `Environment`       | User input                      | `Environment`        |

### SettingsMasterEntry

Represents one row in `*_htl_settings_master`.
One master entry is generated per unique `SettingsHead`.

| Field           | Source                               | Target Column    |
|-----------------|--------------------------------------|------------------|
| `SettingHead`   | JSON parent key (e.g. `Hotel`)       | `SettingHead`    |
| `SettingsType`  | `"S"` (default) or from JSON metadata| `SettingsType`   |
| `RecordStatus`  | Always `1`                           | `RecordStatus`   |
| `Port`          | User input                           | `Port`           |
| `Version`       | User input                           | `Version`        |
| `Environment`   | User input                           | `Environment`    |

### ParameterEntry

Represents one row in `*_htl_params_settings`.

| Field               | Source                              | Target Column        |
|---------------------|-------------------------------------|----------------------|
| `ParamsHead`        | JSON parent key (e.g. `HtlGeneral`) | `ParamsHead`         |
| `ParentHead`        | Parent section if nested / null     | `ParentHead`         |
| `MemberName`        | JSON child key                      | `MemberName`         |
| `MemberValue`       | JSON child value                    | `MemberValue`        |
| `MemberDescription` | Optional / null                     | `MemeberDescription` |
| `MemberDataType`    | Inferred                            | `MemberDataType`     |
| `RecordStatus`      | Always `1`                          | `RecordStatus`       |
| `AUI`               | Optional / null                     | `AUI`                |
| `Port`              | User input                          | `Port`               |
| `Version`           | User input                          | `Version`            |
| `Environment`       | User input                          | `Environment`        |

### ParamsMasterEntry

Represents one row in `*_htl_params_master`.
One master entry is generated per unique `ParamsHead`.

| Field           | Source                                          | Target Column  |
|-----------------|-------------------------------------------------|----------------|
| `ParamsHead`    | JSON parent key (e.g. `HtlGeneral`)             | `ParamsHead`   |
| `SettingHead`   | Parent settings section or inferred association | `SettingHead`  |
| `RecordStatus`  | Always `1`                                      | `RecordStatus` |
| `Port`          | User input                                      | `Port`         |
| `Version`       | User input                                      | `Version`      |
| `Environment`   | User input                                      | `Environment`  |

### DatabaseConfigEntry

Represents one row in `*_htl_databases`.

| Field               | Source                      | Target Column       |
|---------------------|-----------------------------|---------------------|
| `DataBaseType`      | JSON / default `0`          | `DataBaseType`      |
| `Description`       | JSON / null                 | `Description`       |
| `UserName`          | JSON (SENSITIVE)            | `UserName`          |
| `Password`          | JSON (SENSITIVE)            | `Password`          |
| `DataBaseName`      | JSON                        | `DataBaseName`      |
| `Server`            | JSON                        | `Server`            |
| `Provider`          | JSON / null                 | `Provider`          |
| `ActiveStatus`      | JSON / default `1`          | `ActiveStatus`      |
| `ReadEnable`        | JSON / default `1`          | `ReadEnable`        |
| `WriteEnable`       | JSON / default `1`          | `WriteEnable`       |
| `AUI`               | JSON / null                 | `AUI`               |
| `RecordStatus`      | Always `1`                  | `RecordStatus`      |
| `Port`              | User input                  | `Port`              |
| `Version`           | User input                  | `Version`           |
| `Environment`       | User input                  | `Environment`       |
| `ActivePeriodBegin` | JSON datetime / null        | `ActivePeriodBegin` |
| `ActivePeriodEnd`   | JSON datetime / null        | `ActivePeriodEnd`   |

---

## AUTO_INCREMENT Columns (NEVER included in INSERT)

| Table                     | Auto-increment column  |
|---------------------------|------------------------|
| `*_htl_databases`         | `DataBase_ID`          |
| `*_htl_settings_details`  | `Settings_Details_ID`  |
| `*_htl_settings_master`   | `SettingsMasterID`     |
| `*_htl_params_master`     | `ParamsMasterID`       |
| `*_htl_params_settings`   | `Params_Hotel_ID`      |

---

## MemberDataType Inference

| JSON value type   | MemberDataType |
|-------------------|----------------|
| `string`          | `"string"`     |
| `number`          | `"number"`     |
| `boolean`         | `"bool"`       |
| `null`            | `"null"`       |
| `array`           | `"array"`      |
| `object` (nested) | `"object"`     |

Values are always stored as their string representation in `MemberValue`.
Arrays and objects are JSON-serialized as strings.

---

## SQL Generation Order (dependency-friendly)

1. `*_htl_databases`
2. `*_htl_settings_master`
3. `*_htl_settings_details`
4. `*_htl_params_master`
5. `*_htl_params_settings`

---

## Validation Rules Summary

| Check                         | Severity | Detail                                          |
|-------------------------------|----------|-------------------------------------------------|
| Invalid JSON                  | Error    | File cannot be parsed                           |
| Empty JSON / empty object     | Error    | No configuration to process                     |
| Missing Port                  | Error    | Required user input                             |
| Missing Version               | Error    | Required user input                             |
| Missing Environment           | Error    | Required user input                             |
| Invalid Environment           | Error    | Must be BETA or LIVE                            |
| MemberName > 50               | Error    | Exceeds VARCHAR(50)                             |
| MemberValue > 1000 (settings) | Error    | Exceeds VARCHAR(1000) for settings_details      |
| MemberValue > 200 (params)    | Error    | Exceeds VARCHAR(200) for params_settings        |
| SettingsHead > 50             | Error    | Exceeds VARCHAR(50)                             |
| ParamsHead > 45               | Error    | Exceeds VARCHAR(45)                             |
| Version > 5                   | Error    | Exceeds VARCHAR(5)                              |
| Environment > 20              | Error    | Exceeds VARCHAR(20)                             |
| Port > 45                     | Error    | Exceeds VARCHAR(45)                             |
| Duplicate SettingsHead+Member | Warning  | Same setting appears more than once             |
| Duplicate ParamsHead+Member   | Warning  | Same parameter appears more than once           |
| Null MemberValue              | Warning  | Null stored as SQL NULL                         |
| Empty MemberValue             | Warning  | Empty string stored                             |
| Unexpected data type          | Warning  | Value type unexpected for this member           |
| Unmappable section            | Warning  | Section could not be classified — skipped       |

---

## Future Extension Points

When Phase 2 (DB migration) is added, the normalized model feeds into:

```
Normalized Configuration Model
          ↓
    DB Diff Engine  ←  Current DB State (via IConfigurationRepository)
          ↓
  INSERT / UPDATE / NO CHANGE decisions
          ↓
    Migration Engine
          ↓
      Target DB
```

The `IConfigurationRepository` interface can be introduced later without changing
the parser, mapper, or SQL generator.
