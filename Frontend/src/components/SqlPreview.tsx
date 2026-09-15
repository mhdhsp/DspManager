import React, { useState } from 'react';
import { downloadSqlFile } from '../services/configurationApi';

interface SqlPreviewProps {
  sql: string;
  environment: string;
  port: string;
  version: string;
  onReset: () => void;
}

export const SqlPreview: React.FC<SqlPreviewProps> = ({
  sql,
  environment,
  port,
  version,
  onReset,
}) => {
  const [copied, setCopied] = useState(false);
  const [showWarning, setShowWarning] = useState(true);

  const lineCount = sql.split('\n').length;

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(sql);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Fallback for older browsers
      const el = document.createElement('textarea');
      el.value = sql;
      document.body.appendChild(el);
      el.select();
      document.execCommand('copy');
      document.body.removeChild(el);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  };

  const handleDownload = () => {
    downloadSqlFile(sql, environment, port, version);
  };

  return (
    <section className="card sql-card" aria-label="Generated SQL">
      {/* Security warning banner */}
      {showWarning && (
        <div className="security-banner" role="alert">
          <span className="security-banner__icon">&#9888;</span>
          <div className="security-banner__text">
            <strong>Warning:</strong> The generated SQL may contain sensitive credentials
            (database passwords, usernames). Store and transfer this file securely.
          </div>
          <button
            className="security-banner__close"
            onClick={() => setShowWarning(false)}
            aria-label="Dismiss warning"
          >
            &#10005;
          </button>
        </div>
      )}

      <div className="card__header">
        <h2 className="card__title">Generated SQL</h2>
        <div className="sql-meta">
          <span className="sql-meta__stat">{lineCount} lines</span>
          <span className="sql-meta__stat">{(sql.length / 1024).toFixed(1)} KB</span>
        </div>
      </div>

      {/* Action bar */}
      <div className="sql-actions">
        <button
          type="button"
          className="btn btn--primary"
          onClick={handleDownload}
          aria-label={`Download SQL file for ${environment} port ${port} version ${version}`}
        >
          &#8681; Download .sql
        </button>
        <button
          type="button"
          className="btn btn--secondary"
          onClick={handleCopy}
        >
          {copied ? '✓ Copied!' : 'Copy to clipboard'}
        </button>
        <button
          type="button"
          className="btn btn--ghost"
          onClick={onReset}
        >
          &#8617; Start over
        </button>
      </div>

      {/* SQL code block */}
      <div className="sql-preview-wrapper">
        <pre className="sql-preview" aria-label="SQL script content" tabIndex={0}>
          <code>{sql}</code>
        </pre>
      </div>
    </section>
  );
};
