import React, { useRef, useState } from 'react';

interface FileUploaderProps {
  onFileSelected: (file: File) => void;
  selectedFile: File | null;
  disabled?: boolean;
}

export const FileUploader: React.FC<FileUploaderProps> = ({
  onFileSelected,
  selectedFile,
  disabled = false,
}) => {
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragOver, setDragOver] = useState(false);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) onFileSelected(file);
  };

  const handleDrop = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    setDragOver(false);
    if (disabled) return;
    const file = e.dataTransfer.files?.[0];
    if (file) {
      if (!file.name.endsWith('.json')) {
        alert('Only .json files are accepted.');
        return;
      }
      onFileSelected(file);
    }
  };

  const handleDragOver = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    if (!disabled) setDragOver(true);
  };

  const formatBytes = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  return (
    <div className="field-group">
      <label className="field-label" htmlFor="json-upload">
        Configuration File <span className="required">*</span>
      </label>

      <div
        className={`drop-zone ${dragOver ? 'drop-zone--active' : ''} ${disabled ? 'drop-zone--disabled' : ''}`}
        onDrop={handleDrop}
        onDragOver={handleDragOver}
        onDragLeave={() => setDragOver(false)}
        onClick={() => !disabled && inputRef.current?.click()}
        role="button"
        tabIndex={disabled ? -1 : 0}
        aria-label="Upload JSON configuration file"
        onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') inputRef.current?.click(); }}
      >
        <input
          ref={inputRef}
          id="json-upload"
          type="file"
          accept=".json"
          onChange={handleFileChange}
          disabled={disabled}
          style={{ display: 'none' }}
          aria-hidden="true"
        />

        {selectedFile ? (
          <div className="drop-zone__file-info">
            <span className="drop-zone__icon">&#10003;</span>
            <div>
              <div className="drop-zone__filename">{selectedFile.name}</div>
              <div className="drop-zone__meta">{formatBytes(selectedFile.size)}</div>
            </div>
          </div>
        ) : (
          <div className="drop-zone__placeholder">
            <span className="drop-zone__icon drop-zone__icon--upload">&#8679;</span>
            <div className="drop-zone__text">Drop a .json file here or click to browse</div>
          </div>
        )}
      </div>

      {selectedFile && (
        <button
          type="button"
          className="btn btn--ghost btn--sm"
          onClick={(e) => {
            e.stopPropagation();
            if (inputRef.current) inputRef.current.value = '';
            onFileSelected(null as unknown as File);
          }}
          disabled={disabled}
        >
          Remove file
        </button>
      )}
    </div>
  );
};
