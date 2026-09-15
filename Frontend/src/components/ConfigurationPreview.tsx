import React, { useState } from 'react';
import type { NormalisedConfiguration } from '../types/configuration';

interface ConfigurationPreviewProps {
  config: NormalisedConfiguration;
}

type PreviewTab = 'settings' | 'params' | 'databases';

export const ConfigurationPreview: React.FC<ConfigurationPreviewProps> = ({ config }) => {
  const [tab, setTab] = useState<PreviewTab>('settings');

  return (
    <section className="card preview-card" aria-label="Configuration preview">
      <div className="card__header">
        <h2 className="card__title">Configuration Preview</h2>
      </div>

      <div className="tab-bar" role="tablist" aria-label="Preview sections">
        <TabButton id="settings" label={`Settings (${config.settingsDetails.length})`} active={tab === 'settings'} onClick={() => setTab('settings')} />
        <TabButton id="params"   label={`Parameters (${config.paramsSettings.length})`} active={tab === 'params'}   onClick={() => setTab('params')} />
        <TabButton id="databases" label={`Databases (${config.databases.length})`}      active={tab === 'databases'} onClick={() => setTab('databases')} />
      </div>

      <div className="tab-content">
        {tab === 'settings' && (
          <SettingsTable
            details={config.settingsDetails}
            masters={config.settingsMaster}
          />
        )}
        {tab === 'params' && (
          <ParamsTable
            settings={config.paramsSettings}
            masters={config.paramsMaster}
          />
        )}
        {tab === 'databases' && (
          <DatabasesTable databases={config.databases} />
        )}
      </div>
    </section>
  );
};

// ── Tab button ────────────────────────────────────────────────────────────────

const TabButton: React.FC<{
  id: string;
  label: string;
  active: boolean;
  onClick: () => void;
}> = ({ id, label, active, onClick }) => (
  <button
    id={`tab-${id}`}
    role="tab"
    aria-selected={active}
    className={`tab-btn ${active ? 'tab-btn--active' : ''}`}
    onClick={onClick}
  >
    {label}
  </button>
);

// ── Settings table ────────────────────────────────────────────────────────────

const SettingsTable: React.FC<{
  details: NormalisedConfiguration['settingsDetails'];
  masters: NormalisedConfiguration['settingsMaster'];
}> = ({ details, masters }) => {
  if (details.length === 0) return <EmptyState message="No settings entries found." />;

  const heads = Array.from(new Set(details.map((d) => d.settingsHead)));

  return (
    <div className="preview-table-wrapper">
      <table className="preview-table" aria-label="Settings details">
        <thead>
          <tr>
            <th>SettingsHead</th>
            <th>MemberName</th>
            <th>MemberValue</th>
            <th>DataType</th>
            <th>Type</th>
          </tr>
        </thead>
        <tbody>
          {heads.map((head) => {
            const masterType = masters.find((m) => m.settingHead === head)?.settingsType ?? '—';
            return details
              .filter((d) => d.settingsHead === head)
              .map((d, i) => (
                <tr key={`${head}-${d.memberName}-${i}`}>
                  {i === 0 && (
                    <td rowSpan={details.filter((x) => x.settingsHead === head).length} className="head-cell">
                      <strong>{head}</strong>
                    </td>
                  )}
                  <td>{d.memberName}</td>
                  <td className="value-cell">
                    {d.memberValue === null
                      ? <span className="null-value">NULL</span>
                      : d.memberValue === ''
                      ? <span className="empty-value">(empty)</span>
                      : <span title={d.memberValue}>{truncate(d.memberValue, 80)}</span>
                    }
                  </td>
                  <td><code>{d.memberDataType ?? '—'}</code></td>
                  {i === 0 && (
                    <td rowSpan={details.filter((x) => x.settingsHead === head).length}>
                      <code>{masterType}</code>
                    </td>
                  )}
                </tr>
              ));
          })}
        </tbody>
      </table>
    </div>
  );
};

// ── Params table ──────────────────────────────────────────────────────────────

const ParamsTable: React.FC<{
  settings: NormalisedConfiguration['paramsSettings'];
  masters: NormalisedConfiguration['paramsMaster'];
}> = ({ settings, masters }) => {
  if (settings.length === 0) return <EmptyState message="No parameter entries found." />;

  const heads = Array.from(new Set(settings.map((p) => p.paramsHead)));

  return (
    <div className="preview-table-wrapper">
      <table className="preview-table" aria-label="Parameters settings">
        <thead>
          <tr>
            <th>ParamsHead</th>
            <th>SettingHead</th>
            <th>MemberName</th>
            <th>MemberValue</th>
            <th>DataType</th>
          </tr>
        </thead>
        <tbody>
          {heads.map((head) => {
            const settingHead = masters.find((m) => m.paramsHead === head)?.settingHead ?? '—';
            return settings
              .filter((p) => p.paramsHead === head)
              .map((p, i) => (
                <tr key={`${head}-${p.memberName}-${i}`}>
                  {i === 0 && (
                    <td rowSpan={settings.filter((x) => x.paramsHead === head).length} className="head-cell">
                      <strong>{head}</strong>
                    </td>
                  )}
                  {i === 0 && (
                    <td rowSpan={settings.filter((x) => x.paramsHead === head).length}>
                      {settingHead}
                    </td>
                  )}
                  <td>{p.memberName}</td>
                  <td className="value-cell">
                    {p.memberValue === null
                      ? <span className="null-value">NULL</span>
                      : p.memberValue === ''
                      ? <span className="empty-value">(empty)</span>
                      : <span title={p.memberValue}>{truncate(p.memberValue, 80)}</span>
                    }
                  </td>
                  <td><code>{p.memberDataType ?? '—'}</code></td>
                </tr>
              ));
          })}
        </tbody>
      </table>
    </div>
  );
};

// ── Databases table ───────────────────────────────────────────────────────────

const DatabasesTable: React.FC<{
  databases: NormalisedConfiguration['databases'];
}> = ({ databases }) => {
  if (databases.length === 0) return <EmptyState message="No database entries found." />;

  return (
    <div className="preview-table-wrapper">
      <div className="sensitive-warning" role="alert">
        <span className="sensitive-warning__icon">&#9888;</span>
        Sensitive fields (Password, UserName) are present. Values are shown masked below.
      </div>
      <table className="preview-table" aria-label="Database configurations">
        <thead>
          <tr>
            <th>#</th>
            <th>Server</th>
            <th>Database</th>
            <th>UserName</th>
            <th>Password</th>
            <th>Provider</th>
            <th>Type</th>
            <th>Active</th>
          </tr>
        </thead>
        <tbody>
          {databases.map((db, i) => (
            <tr key={i}>
              <td>{i + 1}</td>
              <td>{db.server ?? <span className="null-value">NULL</span>}</td>
              <td>{db.dataBaseName ?? <span className="null-value">NULL</span>}</td>
              <td>
                {db.userName
                  ? <span className="masked-value" title="Sensitive — masked">{maskValue(db.userName)}</span>
                  : <span className="null-value">NULL</span>}
              </td>
              <td>
                {db.password
                  ? <span className="masked-value" title="Sensitive — masked">{'•'.repeat(8)}</span>
                  : <span className="null-value">NULL</span>}
              </td>
              <td>{db.provider ?? <span className="null-value">NULL</span>}</td>
              <td><code>{db.dataBaseType}</code></td>
              <td>{db.activeStatus === 1 ? '✓' : '✗'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

// ── Helpers ───────────────────────────────────────────────────────────────────

const EmptyState: React.FC<{ message: string }> = ({ message }) => (
  <div className="empty-state">{message}</div>
);

function truncate(s: string, max: number): string {
  return s.length > max ? s.slice(0, max) + '…' : s;
}

function maskValue(val: string): string {
  if (val.length <= 2) return '••';
  return val[0] + '•'.repeat(Math.min(val.length - 2, 6)) + val[val.length - 1];
}
