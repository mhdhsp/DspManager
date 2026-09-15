import React, { useState } from 'react';
import type { ValidationIssue } from '../types/configuration';

interface ValidationIssuesProps {
  issues: ValidationIssue[];
}

export const ValidationIssues: React.FC<ValidationIssuesProps> = ({ issues }) => {
  const [filter, setFilter] = useState<'all' | 'Error' | 'Warning'>('all');

  if (issues.length === 0) return null;

  const errors   = issues.filter((i) => i.severity === 'Error');
  const warnings = issues.filter((i) => i.severity === 'Warning');

  const visible =
    filter === 'all'    ? issues :
    filter === 'Error'  ? errors :
                          warnings;

  return (
    <section className="card issues-card" aria-label="Validation issues">
      <div className="card__header">
        <h2 className="card__title">Validation Issues</h2>
        <div className="issues-badges">
          {errors.length > 0 && (
            <span className="badge badge--error">{errors.length} error{errors.length !== 1 ? 's' : ''}</span>
          )}
          {warnings.length > 0 && (
            <span className="badge badge--warning">{warnings.length} warning{warnings.length !== 1 ? 's' : ''}</span>
          )}
        </div>
      </div>

      {/* Filter tabs */}
      <div className="issues-filter" role="tablist" aria-label="Filter issues">
        {(['all', 'Error', 'Warning'] as const).map((f) => (
          <button
            key={f}
            role="tab"
            aria-selected={filter === f}
            className={`filter-tab ${filter === f ? 'filter-tab--active' : ''}`}
            onClick={() => setFilter(f)}
          >
            {f === 'all' ? `All (${issues.length})` :
             f === 'Error' ? `Errors (${errors.length})` :
             `Warnings (${warnings.length})`}
          </button>
        ))}
      </div>

      <ul className="issues-list" role="list">
        {visible.map((issue, idx) => (
          <li
            key={idx}
            className={`issue-item issue-item--${issue.severity.toLowerCase()}`}
            role="listitem"
          >
            <span className={`issue-badge issue-badge--${issue.severity.toLowerCase()}`} aria-label={issue.severity}>
              {issue.severity === 'Error' ? '✕' : '⚠'}
            </span>
            <div className="issue-body">
              <div className="issue-message">{issue.message}</div>
              {issue.context && (
                <div className="issue-context">
                  <span className="issue-context__label">Location: </span>
                  <code className="issue-context__value">{issue.context}</code>
                </div>
              )}
              <div className="issue-code">{issue.code}</div>
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
};
