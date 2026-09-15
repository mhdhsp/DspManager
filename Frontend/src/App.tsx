import React from 'react';
import { ConfigurationAnalyzer } from './pages/ConfigurationAnalyzer';
import './styles.css';

const App: React.FC = () => {
  return (
    <div className="app">
      <header className="app-header">
        <div className="app-header__inner">
          <div className="app-header__brand">
            <span className="app-header__logo" aria-hidden="true">&#9670;</span>
            <div>
              <h1 className="app-header__title">Hotel Config Analyser</h1>
              <p className="app-header__subtitle">
                JSON &rarr; Analyse &rarr; MySQL SQL Generator &nbsp;&bull;&nbsp; Phase 1
              </p>
            </div>
          </div>
          <div className="app-header__meta">
            <span className="phase-badge">Phase 1</span>
            <span className="phase-badge phase-badge--muted">No DB connection</span>
          </div>
        </div>
      </header>

      <main className="app-main" id="main-content">
        <ConfigurationAnalyzer />
      </main>

      <footer className="app-footer">
        <div className="app-footer__inner">
          <span>Hotel Config Analyser &mdash; Internal tool &mdash; Phase 1 (Analyse + SQL Generator only)</span>
        </div>
      </footer>
    </div>
  );
};

export default App;
