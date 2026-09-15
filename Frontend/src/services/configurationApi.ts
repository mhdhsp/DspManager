import type { AnalysisResult, AnalyzeFormValues, NormalisedConfiguration } from '../types/configuration';

// When VITE_API_BASE_URL is empty the Vite dev-server proxy forwards /api/*
// to the backend automatically. For production builds set the env var to the
// actual backend origin (e.g. https://api.example.com).
const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '';

// ── Analyse ───────────────────────────────────────────────────────────────────

/**
 * POST /api/configuration/analyze
 * Sends the JSON file and user inputs as multipart/form-data.
 * Returns the full AnalysisResult from the backend.
 */
export async function analyzeConfiguration(values: AnalyzeFormValues): Promise<AnalysisResult> {
  const form = new FormData();
  form.append('file', values.file, values.file.name);
  form.append('port', values.port);
  form.append('version', values.version);
  form.append('environment', values.environment);
  form.append('includeTransaction', String(values.includeTransaction));

  const response = await fetch(`${API_BASE}/api/configuration/analyze`, {
    method: 'POST',
    body: form,
    // Do NOT set Content-Type header — browser sets it with the multipart boundary
  });

  if (!response.ok) {
    const problem = await tryParseProblem(response);
    throw new ApiError(response.status, problem ?? `Analyze failed (HTTP ${response.status})`);
  }

  return response.json() as Promise<AnalysisResult>;
}

// ── Generate SQL ──────────────────────────────────────────────────────────────

/**
 * POST /api/configuration/generate-sql
 * Sends the normalised configuration back to the backend.
 * Returns the generated SQL text.
 */
export async function generateSql(config: NormalisedConfiguration): Promise<string> {
  const response = await fetch(`${API_BASE}/api/configuration/generate-sql`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ config }),
  });

  if (!response.ok) {
    const problem = await tryParseProblem(response);
    throw new ApiError(response.status, problem ?? `SQL generation failed (HTTP ${response.status})`);
  }

  // The response is a downloadable file — read it as text
  return response.text();
}

// ── Download helper ───────────────────────────────────────────────────────────

/**
 * Triggers a browser download of the provided SQL text.
 * The filename follows the convention: hotel-config-{ENV}-{PORT}-v{VERSION}.sql
 */
export function downloadSqlFile(
  sqlContent: string,
  environment: string,
  port: string,
  version: string,
): void {
  const safeEnv     = sanitise(environment);
  const safePort    = sanitise(port);
  const safeVersion = sanitise(version);
  const filename    = `hotel-config-${safeEnv}-${safePort}-v${safeVersion}.sql`;

  const blob = new Blob([sqlContent], { type: 'application/sql' });
  const url  = URL.createObjectURL(blob);

  const anchor       = document.createElement('a');
  anchor.href        = url;
  anchor.download    = filename;
  anchor.style.display = 'none';

  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);

  // Revoke the object URL after a short delay
  setTimeout(() => URL.revokeObjectURL(url), 5000);
}

// ── Helpers ───────────────────────────────────────────────────────────────────

export class ApiError extends Error {
  constructor(public readonly statusCode: number, message: string) {
    super(message);
    this.name = 'ApiError';
  }
}

async function tryParseProblem(response: Response): Promise<string | null> {
  try {
    const ct = response.headers.get('content-type') ?? '';
    if (ct.includes('application/json') || ct.includes('application/problem+json')) {
      const body = await response.json();
      return body?.detail ?? body?.title ?? null;
    }
    const text = await response.text();
    return text.length > 0 ? text : null;
  } catch {
    return null;
  }
}

function sanitise(value: string): string {
  return value.replace(/[^a-zA-Z0-9\-_.]/g, '_');
}
