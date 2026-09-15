import React from 'react';
import type { Environment } from '../types/configuration';

interface ConfigurationInputsProps {
  port: string;
  version: string;
  environment: Environment;
  includeTransaction: boolean;
  onPortChange: (val: string) => void;
  onVersionChange: (val: string) => void;
  onEnvironmentChange: (val: Environment) => void;
  onIncludeTransactionChange: (val: boolean) => void;
  disabled?: boolean;
  errors?: Partial<Record<'port' | 'version' | 'environment', string>>;
}

export const ConfigurationInputs: React.FC<ConfigurationInputsProps> = ({
  port,
  version,
  environment,
  includeTransaction,
  onPortChange,
  onVersionChange,
  onEnvironmentChange,
  onIncludeTransactionChange,
  disabled = false,
  errors = {},
}) => {
  return (
    <div className="inputs-grid">
      {/* Port */}
      <div className="field-group">
        <label className="field-label" htmlFor="port">
          Port <span className="required">*</span>
        </label>
        <input
          id="port"
          type="text"
          className={`text-input ${errors.port ? 'text-input--error' : ''}`}
          value={port}
          onChange={(e) => onPortChange(e.target.value)}
          placeholder="e.g. 80"
          disabled={disabled}
          maxLength={45}
          aria-describedby={errors.port ? 'port-error' : undefined}
        />
        {errors.port && (
          <span id="port-error" className="field-error" role="alert">
            {errors.port}
          </span>
        )}
      </div>

      {/* Version */}
      <div className="field-group">
        <label className="field-label" htmlFor="version">
          Version <span className="required">*</span>
        </label>
        <input
          id="version"
          type="text"
          className={`text-input ${errors.version ? 'text-input--error' : ''}`}
          value={version}
          onChange={(e) => onVersionChange(e.target.value)}
          placeholder="e.g. 6.0"
          disabled={disabled}
          maxLength={5}
          aria-describedby={errors.version ? 'version-error' : undefined}
        />
        {errors.version && (
          <span id="version-error" className="field-error" role="alert">
            {errors.version}
          </span>
        )}
      </div>

      {/* Environment */}
      <div className="field-group">
        <label className="field-label" htmlFor="environment">
          Environment <span className="required">*</span>
        </label>
        <select
          id="environment"
          className={`select-input ${errors.environment ? 'select-input--error' : ''}`}
          value={environment}
          onChange={(e) => onEnvironmentChange(e.target.value as Environment)}
          disabled={disabled}
          aria-describedby={errors.environment ? 'env-error' : undefined}
        >
          <option value="BETA">BETA</option>
          <option value="LIVE">LIVE</option>
        </select>
        {errors.environment && (
          <span id="env-error" className="field-error" role="alert">
            {errors.environment}
          </span>
        )}
      </div>

      {/* Include Transaction */}
      <div className="field-group field-group--inline">
        <label className="checkbox-label" htmlFor="include-transaction">
          <input
            id="include-transaction"
            type="checkbox"
            className="checkbox-input"
            checked={includeTransaction}
            onChange={(e) => onIncludeTransactionChange(e.target.checked)}
            disabled={disabled}
          />
          Include transaction (START TRANSACTION / COMMIT)
        </label>
      </div>
    </div>
  );
};
