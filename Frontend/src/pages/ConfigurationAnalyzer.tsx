import React, { useState, useCallback } from 'react';
import { FileUploader } from '../components/FileUploader';
import { ConfigurationInputs } from '../components/ConfigurationInputs';
import { AnalysisSummary } from '../components/AnalysisSummary';
import { ValidationIssues } from '../components/ValidationIssues';
import { ConfigurationPreview } from '../components/ConfigurationPreview';
import { SqlPreview } from '../components/SqlPreview';
import {
  analyzeConfiguration,
  generateSql,
  ApiError,
} from '../services/configurationApi';
import type {
  AnalysisResult,
  AnalysisStep,
  Environment,
} from '../types/configuration';

// ── Local form state ──────────────────────────────────────────────────────────

interface FormState {
  file: File | null;
  port: string;
  version: string;
  environment: Environment;
  includeTransaction: boolean;
}

interface FormErrors {
  file?: string;
  port?: string;
  version?: string;
  environment?: string;
}

// ── Page component ────────────────────────────────────────────────────────────

export const ConfigurationAnalyzer: React.FC = () => {
  // Form
  const [form, setForm] = useState<FormState>({
    file: null,
    port: '',
    version: '',
    environment: 'BETA',
    includeTransaction: true,
  });
  const [formErrors, setFormErrors] = useState<FormErrors>({});

  // Pipeline state
  const [step, setStep]             = useState<AnalysisStep>('input');
  const [result, setResult]         = useState<AnalysisResult | null>(null);
  const [sqlContent, setSqlContent] = useState<string | null>(null);
  const [globalError, setGlobalError] = useState<string | null>(null);

  // ── Form helpers ────────────────────────────────────────────────────────────

  const setField = useCallback(
    <K extends keyof FormState>(key: K, value: FormState[K]) => {
      setForm((prev) => ({ ...prev, [key]: value }));
      setFormErrors((prev) => ({ ...prev, [key]: undefined }));
    },
    [],
  );

  const validateForm = (): FormErrors => {
    const errors: FormErrors = {};
    if (!form.file)                     errors.file    = 'A JSON configuration file is required.';
    if (!form.port.trim())              errors.port    = 'Port is required.';
    else if (form.port.trim().length > 45) errors.port = 'Port must be 45 characters or fewer.';
    if (!form.version.trim())           errors.version = 'Version is required.';
    else if (form.version.trim().length > 5) errors.version = 'Version must be 5 characters or fewer.';
    return errors;
  };

  // ── Handlers ────────────────────────────────────────────────────────────────

  const handleAnalyze = async () => {
    const errors = validateForm();
    if (Object.keys(errors).length > 0) {
      setFormErrors(errors);
      return;
    }

    setGlobalError(null);
    setStep('analysing');

    try {
      const analysisResult = await analyzeConfiguration({
        file: form.file!,
        port: form.port.trim(),
        version: form.version.trim(),
        environment: form.environment,
        includeTransaction: form.includeTransaction,
      });
      setResult(analysisResult);
      setStep('analysed');
    } catch (err) {
      const message = err instanceof ApiError
        ? err.message
        : 'An unexpected error occurred while analysing the configuration.';
      setGlobalError(message);
      setStep('input');
    }
  };

  const handleGenerateSql = async () => {
    if (!result?.normalisedConfig) return;

    setGlobalError(null);
    setStep('generating');

    try {
      const sql = await generateSql(result.normalisedConfig);
      setSqlContent(sql);
      setStep('generated');
    } catch (err) {
      const message = err instanceof ApiError
        ? err.message
        : 'An unexpected error occurred while generating SQL.';
      setGlobalError(message);
      setStep('analysed');
    }
  };

  const handleReset = () => {
    setForm({ file: null, port: '', version: '', environment: 'BETA', includeTransaction: true });
    setFormErrors({});
    setResult(null);
    setSqlContent(null);
    setGlobalError(null);
    setStep('input');
  };

  const handleBackToAnalysis = () => {
    setSqlContent(null);
    setStep('analysed');
  };

  // ── Derived state ───────────────────────────────────────────────────────────

  const isLoading  = step === 'analysing' || step === 'generating';
  const isAnalysed = step === 'analysed'  || step === 'generated';

  // ── Render ──────────────────────────────────────────────────────────────────

  return (
    <div className="page-layout">

      {/* ── Step indicator ── */}
      <StepIndicator step={step} />

      {/* ── Global error ── */}
      {globalError && (
        <div className="alert alert--error" role="alert">
          <strong>Error: </strong>{globalError}
          <button className="alert__close" onClick={() => setGlobalError(null)} aria-label="Dismiss">✕</button>
        </div>
      )}

      {/* ── Input form ── */}
      {(step === 'input' || step === 'analysing') && (
        <section className="card input-card">
          <div className="card__header">
            <h2 className="card__title">Upload Configuration</h2>
          </div>

          <FileUploader
            onFileSelected={(file) => setField('file', file)}
            selectedFile={form.file}
            disabled={isLoading}
          />
          {formErrors.file && (
            <span className="field-error" role="alert">{formErrors.file}</span>
          )}

          <ConfigurationInputs
            port={form.port}
            version={form.version}
            environment={form.environment}
            includeTransaction={form.includeTransaction}
            onPortChange={(v) => setField('port', v)}
            onVersionChange={(v) => setField('version', v)}
            onEnvironmentChange={(v) => setField('environment', v)}
            onIncludeTransactionChange={(v) => setField('includeTransaction', v)}
            disabled={isLoading}
            errors={formErrors}
          />

          <div className="form-actions">
            <button
              type="button"
              className="btn btn--primary btn--lg"
              onClick={handleAnalyze}
              disabled={isLoading}
            >
              {step === 'analysing' ? (
                <><Spinner /> Analysing…</>
              ) : (
                'Analyse Configuration'
              )}
            </button>
          </div>
        </section>
      )}

      {/* ── Analysis results ── */}
      {isAnalysed && result && step !== 'generated' && (
        <>
          <AnalysisSummary result={result} />

          {result.issues.length > 0 && (
            <ValidationIssues issues={result.issues} />
          )}

          {result.normalisedConfig && (
            <ConfigurationPreview config={result.normalisedConfig} />
          )}

          <div className="form-actions form-actions--analysis">
            <button
              type="button"
              className="btn btn--ghost"
              onClick={handleReset}
              disabled={isLoading}
            >
              &#8617; Start over
            </button>

            <button
              type="button"
              className="btn btn--primary btn--lg"
              onClick={handleGenerateSql}
              disabled={!result.canGenerateSql || isLoading}
              title={!result.canGenerateSql ? 'Resolve all errors before generating SQL' : undefined}
            >
              {step === 'generating' ? (
                <><Spinner /> Generating SQL…</>
              ) : (
                'Generate SQL'
              )}
            </button>

            {!result.canGenerateSql && (
              <span className="blocked-hint">
                SQL generation is disabled — resolve all errors above first.
              </span>
            )}
          </div>
        </>
      )}

      {/* ── SQL output ── */}
      {step === 'generated' && sqlContent && result && (
        <>
          <AnalysisSummary result={result} />

          <SqlPreview
            sql={sqlContent}
            environment={result.environment}
            port={result.port}
            version={result.version}
            onReset={handleReset}
          />

          <div className="form-actions">
            <button
              type="button"
              className="btn btn--ghost"
              onClick={handleBackToAnalysis}
            >
              &#8592; Back to analysis
            </button>
          </div>
        </>
      )}
    </div>
  );
};

// ── Sub-components ────────────────────────────────────────────────────────────

const STEPS: { key: AnalysisStep; label: string }[] = [
  { key: 'input',     label: 'Upload & Configure' },
  { key: 'analysing', label: 'Upload & Configure' },
  { key: 'analysed',  label: 'Review Analysis' },
  { key: 'generating', label: 'Review Analysis' },
  { key: 'generated', label: 'Download SQL' },
];

const STEP_ORDER: AnalysisStep[] = ['input', 'analysed', 'generated'];

const StepIndicator: React.FC<{ step: AnalysisStep }> = ({ step }) => {
  const current = step === 'analysing' ? 'input'
    : step === 'generating' ? 'analysed'
    : step;

  return (
    <nav className="step-indicator" aria-label="Progress">
      {STEP_ORDER.map((s, i) => {
        const idx     = STEP_ORDER.indexOf(current);
        const isDone  = i < idx;
        const isActive = s === current;
        return (
          <React.Fragment key={s}>
            <div className={`step ${isActive ? 'step--active' : isDone ? 'step--done' : 'step--pending'}`}>
              <span className="step__num">{isDone ? '✓' : i + 1}</span>
              <span className="step__label">{STEPS.find((x) => x.key === s)?.label}</span>
            </div>
            {i < STEP_ORDER.length - 1 && <div className={`step-connector ${isDone ? 'step-connector--done' : ''}`} />}
          </React.Fragment>
        );
      })}
    </nav>
  );
};

const Spinner: React.FC = () => (
  <span className="spinner" role="status" aria-label="Loading" />
);
