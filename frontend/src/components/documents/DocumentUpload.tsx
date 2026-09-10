import { useId, useRef, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { documentsApi } from '../../api/documents';
import { DOCUMENT_KINDS, type Document, type DocumentKind, type DuplicateDocument } from '../../types/documents';
import { DOCUMENT_KIND_LABELS, MAX_FILE_BYTES, duplicateOf, formatBytes, preflightFile, serverMessageOf } from './documentFormat';

interface Props {
  dashboardId: number;
  /** Posé : le document part rattaché à cette échéance. */
  echeanceId?: number | null;
  onUploaded: (document: Document) => void;
  /** Sur un 409 : ouvrir le document déjà rangé. Appelé dans le geste, pour que l'onglet puisse s'ouvrir. */
  onOpenExisting?: (id: number, name: string) => void;
  onCancel?: () => void;
}

/** « 2026 » → 2026, vide → null. Le serveur borne la plage (1990 à 2100). */
const parseYear = (raw: string): number | null => {
  const trimmed = raw.trim();
  if (!trimmed) return null;
  const n = Number(trimmed);
  return Number.isInteger(n) ? n : null;
};

/**
 * Le composant unique de dépôt : un choix de fichier sans `capture` (l'iPhone propose alors « Prendre une
 * photo | Photothèque | Choisir un fichier »), la nature, l'année fiscale, une barre de progression. Le
 * fichier vit dans l'état local le temps de l'envoi et nulle part ensuite : la mutation est remise à zéro
 * une fois réglée et son cache ne le garde pas (gcTime 0).
 */
export const DocumentUpload = ({ dashboardId, echeanceId = null, onUploaded, onOpenExisting, onCancel }: Props) => {
  const queryClient = useQueryClient();
  const inputId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [kind, setKind] = useState<DocumentKind>('Facture');
  const [fiscalYear, setFiscalYear] = useState(String(new Date().getFullYear()));
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [duplicate, setDuplicate] = useState<DuplicateDocument | null>(null);

  const clearFile = () => {
    setFile(null);
    if (inputRef.current) inputRef.current.value = '';
  };

  const onPick = (e: React.ChangeEvent<HTMLInputElement>) => {
    const picked = e.target.files?.[0] ?? null;
    setError(null);
    setDuplicate(null);
    if (!picked) { setFile(null); return; }
    const refusal = preflightFile(picked);
    if (refusal) { clearFile(); setError(refusal); return; }
    setFile(picked);
  };

  const upload = useMutation({
    gcTime: 0,
    mutationFn: (f: File) =>
      documentsApi.upload(
        { file: f, dashboardId, kind, fiscalYear: parseYear(fiscalYear), echeanceId },
        (e) => setProgress(e.total ? Math.round((e.loaded * 100) / e.total) : null),
      ),
    onSuccess: async (res) => {
      clearFile();
      setProgress(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['documents', dashboardId] }),
        echeanceId != null ? queryClient.invalidateQueries({ queryKey: ['echeance', echeanceId] }) : Promise.resolve(),
      ]);
      onUploaded(res.data);
    },
    onError: (err) => {
      setProgress(null);
      const dup = duplicateOf(err);
      if (dup) { clearFile(); setDuplicate(dup); return; }
      setError(serverMessageOf(err, 'Envoi impossible, réessaie.'));
    },
    onSettled: () => { upload.reset(); },
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!file || upload.isPending) return;
    setError(null);
    setDuplicate(null);
    setProgress(0);
    upload.mutate(file);
  };

  const busy = upload.isPending;

  return (
    <form onSubmit={handleSubmit} className="space-y-3" noValidate>
      <div>
        <input
          ref={inputRef}
          id={inputId}
          type="file"
          accept="application/pdf,image/jpeg,image/png"
          onChange={onPick}
          disabled={busy}
          className="sr-only"
        />
        {file ? (
          <div className="flex items-center justify-between gap-3 min-h-11 px-3 rounded-lg bg-white/5 border border-white/10">
            <span className="min-w-0 text-sm text-white break-words">
              {file.name} <span className="text-white/40 whitespace-nowrap">· {formatBytes(file.size)}</span>
            </span>
            <label htmlFor={inputId} className="shrink-0 min-h-11 inline-flex items-center px-2 text-sm text-white/50 hover:text-white cursor-pointer">
              Changer
            </label>
          </div>
        ) : (
          <label
            htmlFor={inputId}
            className="w-full min-h-11 inline-flex items-center justify-center rounded-lg border border-dashed border-white/20 bg-white/5 text-sm text-white/70 hover:text-white hover:bg-white/10 cursor-pointer transition-colors"
          >
            Choisir un fichier ou prendre une photo
          </label>
        )}
        <p className="text-xs text-white/30 mt-1">PDF, JPEG ou PNG, jusqu'à {formatBytes(MAX_FILE_BYTES)}.</p>
      </div>

      <div role="group" aria-label="Nature du document" className="flex flex-wrap gap-1.5">
        {DOCUMENT_KINDS.map((k) => (
          <button
            key={k}
            type="button"
            aria-pressed={kind === k}
            onClick={() => setKind(k)}
            disabled={busy}
            className={`min-h-9 px-3 rounded-full text-sm border transition-colors disabled:opacity-50 ${
              kind === k
                ? 'bg-amber-500/20 text-amber-300 border-amber-500/30'
                : 'text-white/60 border-white/10 hover:text-white hover:bg-white/5'
            }`}
          >
            {DOCUMENT_KIND_LABELS[k]}
          </button>
        ))}
      </div>

      <div className="flex items-center gap-3">
        <label htmlFor={`${inputId}-year`} className="text-sm text-white/40 shrink-0">
          Année fiscale <span className="text-white/30">(facultatif)</span>
        </label>
        <input
          id={`${inputId}-year`}
          type="number"
          inputMode="numeric"
          min={1990}
          max={2100}
          value={fiscalYear}
          onChange={(e) => setFiscalYear(e.target.value)}
          disabled={busy}
          className="w-24 min-h-11 px-3 rounded-lg bg-white/5 border border-white/10 text-white text-sm tabular-nums focus:outline-none focus:border-amber-500/50 disabled:opacity-50"
        />
      </div>

      {progress != null && (
        <div role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={progress} aria-label="Envoi" className="h-1.5 rounded-full bg-white/10 overflow-hidden">
          <div className="h-full bg-amber-400 transition-[width] duration-200" style={{ width: `${progress}%` }} />
        </div>
      )}

      {error && <p className="text-xs text-amber-300/90 break-words">{error}</p>}
      {duplicate && (
        <p className="text-xs text-amber-300/90 break-words">
          {duplicate.message}
          {onOpenExisting && (
            <>
              {' · '}
              <button
                type="button"
                onClick={() => onOpenExisting(duplicate.existingDocumentId, 'Document existant')}
                className="underline underline-offset-2 hover:text-white transition-colors"
              >
                Ouvrir le document existant
              </button>
            </>
          )}
        </p>
      )}

      <div className="flex gap-2">
        <button
          type="submit"
          disabled={!file || busy}
          className="flex-1 min-h-11 rounded-xl bg-amber-500/20 text-amber-300 border border-amber-500/30 font-medium hover:bg-amber-500/30 disabled:opacity-50 transition-colors"
        >
          {busy ? (progress != null && progress < 100 ? `Envoi… ${progress} %` : 'Envoi…') : 'Envoyer'}
        </button>
        {onCancel && (
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="min-h-11 px-4 rounded-xl border border-white/10 text-white/60 hover:text-white hover:bg-white/5 disabled:opacity-50 transition-colors"
          >
            Annuler
          </button>
        )}
      </div>
    </form>
  );
};
