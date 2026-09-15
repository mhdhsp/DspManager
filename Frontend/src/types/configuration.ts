// ── Shared enums ──────────────────────────────────────────────────────────────

export type Environment = 'BETA' | 'LIVE';
export type IssueSeverity = 'Warning' | 'Error';

// ── API request shapes ────────────────────────────────────────────────────────

export interface AnalyzeFormValues {
  file: File;
  port: string;
  version: string;
  environment: Environment;
  includeTransaction: boolean;
}

// ── API response shapes (mirror the C# models) ────────────────────────────────

export interface ValidationIssue {
  severity: IssueSeverity;
  code: string;
  message: string;
  context: string | null;
}

export interface TableAnalysisSummary {
  tableName: string;
  recordCount: number;
  hasErrors: boolean;
  status: string;
}

// Normalised row types (used in the preview table)
export interface SettingEntry {
  settingsHead: string;
  memberName: string;
  memberValue: string | null;
  memberDescription: string | null;
  memberDataType: string | null;
  recordStatus: number;
  aui: string | null;
  port: string;
  version: string;
  environment: string;
}

export interface SettingsMasterEntry {
  settingHead: string;
  settingsType: string;
  recordStatus: number;
  port: string;
  version: string;
  environment: string;
}

export interface ParameterEntry {
  paramsHead: string;
  parentHead: string | null;
  memberName: string;
  memberValue: string | null;
  memberDescription: string | null;
  memberDataType: string | null;
  recordStatus: number;
  aui: string | null;
  port: string;
  version: string;
  environment: string;
}

export interface ParamsMasterEntry {
  paramsHead: string;
  settingHead: string;
  recordStatus: number;
  port: string;
  version: string;
  environment: string;
}

export interface DatabaseConfigEntry {
  dataBaseType: number;
  description: string | null;
  /** Sensitive — displayed as masked in UI */
  userName: string | null;
  /** Sensitive — never display raw value */
  password: string | null;
  dataBaseName: string | null;
  server: string | null;
  provider: string | null;
  activeStatus: number;
  readEnable: number;
  writeEnable: number;
  aui: string | null;
  recordStatus: number;
  port: string;
  version: string;
  environment: string;
  activePeriodBegin: string | null;
  activePeriodEnd: string | null;
}

export interface NormalisedConfiguration {
  port: string;
  version: string;
  environment: string;
  fileName: string;
  includeTransaction: boolean;
  databases: DatabaseConfigEntry[];
  settingsMaster: SettingsMasterEntry[];
  settingsDetails: SettingEntry[];
  paramsMaster: ParamsMasterEntry[];
  paramsSettings: ParameterEntry[];
  parseIssues: ValidationIssue[];
}

export interface AnalysisResult {
  fileName: string;
  port: string;
  version: string;
  environment: string;
  includeTransaction: boolean;

  databasesCount: number;
  settingsMasterCount: number;
  settingsDetailsCount: number;
  paramsMasterCount: number;
  paramsSettingsCount: number;

  issues: ValidationIssue[];
  warningCount: number;
  errorCount: number;
  canGenerateSql: boolean;

  tableSummaries: TableAnalysisSummary[];
  settingsHeadsFound: string[];
  paramsHeadsFound: string[];

  /** The normalised config — echoed to generate-sql */
  normalisedConfig: NormalisedConfiguration | null;
}

// ── UI state ──────────────────────────────────────────────────────────────────

export type AnalysisStep = 'input' | 'analysing' | 'analysed' | 'generating' | 'generated';

export interface AppState {
  step: AnalysisStep;
  analysisResult: AnalysisResult | null;
  sqlContent: string | null;
  error: string | null;
}
