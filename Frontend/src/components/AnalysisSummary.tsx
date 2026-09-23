import React from 'react';
import type { AnalysisResult } from '../types/configuration';

interface AnalysisSummaryProps {
  result: AnalysisResult;
}

export const AnalysisSummary: React.FC<AnalysisSummaryProps> = ({ result }) => {
  const totalRows =
    result.databasesCount +
    result.settingsMasterCount +
    result.settingsDetailsCount +
    result.paramsMasterCount +
    result.paramsSettingsCount;

  return (
    <section className="card summary-card" aria-label="Analysis summary">
      <div className="card__header">
        <h2 className="card__title">Configuration Analysis</h2>
        <span className={`status-pill ${result.errorCount === 0 ? 'status-pill--ok' : 'status-pill--warning'}`}>
          {result.errorCount === 0
            ? 'Ready — no errors'
            : `${result.errorCount} issue${result.errorCount !== 1 ? 's' : ''} — SQL will still be generated`}
        </span>
      </div>

      {/* Meta */}
      <dl className="meta-grid">
        <div className="meta-item">
          <dt className="meta-label">File</dt>
          <dd className="meta-value">{result.fileName}</dd>
        </div>
        <div className="meta-item">
          <dt className="meta-label">Port</dt>
          <dd className="meta-value"><code>{result.port}</code></dd>
        </div>
        <div className="meta-item">
          <dt className="meta-label">Version</dt>
          <dd className="meta-value"><code>{result.version}</code></dd>
        </div>
        <div className="meta-item">
          <dt className="meta-label">Environment</dt>
          <dd className="meta-value">
            <span className={`env-badge env-badge--${result.environment.toLowerCase()}`}>
              {result.environment}
            </span>
          </dd>
        </div>
        <div className="meta-item">
          <dt className="meta-label">Transaction</dt>
          <dd className="meta-value">{result.includeTransaction ? 'Yes' : 'No'}</dd>
        </div>
      </dl>

      <hr className="divider" />

      {/* Row counts */}
      <h3 className="section-label">Record Counts</h3>
      <div className="counts-grid">
        <CountCard label="Databases"       count={result.databasesCount} />
        <CountCard label="Settings Master" count={result.settingsMasterCount} />
        <CountCard label="Settings Detail" count={result.settingsDetailsCount} />
        <CountCard label="Params Master"   count={result.paramsMasterCount} />
        <CountCard label="Params Settings" count={result.paramsSettingsCount} />
        <CountCard label="Total rows"      count={totalRows} highlight />
      </div>

      <hr className="divider" />

      {/* Issue counts */}
      <div className="issue-counts">
        <span className={`issue-count ${result.errorCount > 0 ? 'issue-count--error' : 'issue-count--ok'}`}>
          {result.errorCount} error{result.errorCount !== 1 ? 's' : ''}
        </span>
        <span className={`issue-count ${result.warningCount > 0 ? 'issue-count--warning' : 'issue-count--ok'}`}>
          {result.warningCount} warning{result.warningCount !== 1 ? 's' : ''}
        </span>
      </div>

      <hr className="divider" />

      {/* Table summaries */}
      <h3 className="section-label">Target Tables</h3>
      <table className="summary-table" aria-label="Target table summaries">
        <thead>
          <tr>
            <th>Table</th>
            <th className="text-right">Records</th>
            <th>Status</th>
          </tr>
        </thead>
        <tbody>
          {result.tableSummaries.map((t) => (
            <tr key={t.tableName}>
              <td><code>{t.tableName}</code></td>
              <td className="text-right">{t.recordCount}</td>
              <td>
                <span className={`status-dot ${t.hasErrors ? 'status-dot--error' : 'status-dot--ok'}`}>
                  {t.status}
                </span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {/* Sections found */}
      {(result.settingsHeadsFound.length > 0 || result.paramsHeadsFound.length > 0) && (
        <>
          <hr className="divider" />
          <h3 className="section-label">Sections Detected</h3>
          <div className="sections-row">
            {result.settingsHeadsFound.length > 0 && (
              <div className="sections-group">
                <div className="sections-group__label">Settings</div>
                <div className="tag-list">
                  {result.settingsHeadsFound.map((h) => (
                    <span key={h} className="tag">{h}</span>
                  ))}
                </div>
              </div>
            )}
            {result.paramsHeadsFound.length > 0 && (
              <div className="sections-group">
                <div className="sections-group__label">Parameters</div>
                <div className="tag-list">
                  {result.paramsHeadsFound.map((h) => (
                    <span key={h} className="tag tag--param">{h}</span>
                  ))}
                </div>
              </div>
            )}
          </div>
        </>
      )}
    </section>
  );
};

// ── Small helper component ────────────────────────────────────────────────────

const CountCard: React.FC<{ label: string; count: number; highlight?: boolean }> = ({
  label,
  count,
  highlight = false,
}) => (
  <div className={`count-card ${highlight ? 'count-card--highlight' : ''}`}>
    <div className="count-card__value">{count}</div>
    <div className="count-card__label">{label}</div>
  </div>
);
