import { useState } from 'react';
import { useDocumentsQuery } from '../../hooks/queries';
import { formatInstantDay } from '../agenda/agendaFormat';
import { DocumentUpload } from '../documents/DocumentUpload';
import { DocumentViewer } from '../documents/DocumentViewer';
import { DOCUMENT_KIND_LABELS, formatBytes } from '../documents/documentFormat';
import { useOpenDocument } from '../documents/useOpenDocument';

interface Props {
  echeanceId: number;
  dashboardId: number;
}

/**
 * Section « Documents » de la feuille Échéance : les pièces rattachées (nom, taille, date), chacune
 * s'ouvre au toucher, et « Ajouter un document » déplie le dépôt en place, le document partant avec
 * l'échéance posée.
 */
export const EcheanceDocumentsSection = ({ echeanceId, dashboardId }: Props) => {
  const { data, isLoading, isError, refetch } = useDocumentsQuery(dashboardId, { echeanceId });
  const [adding, setAdding] = useState(false);
  const { open, pendingId, error: openError, viewer, closeViewer } = useOpenDocument();

  let body: React.ReactNode;
  if (isLoading) {
    body = <p className="text-sm text-white/40 min-h-10 flex items-center">…</p>;
  } else if (isError || !data) {
    body = (
      <p className="text-sm text-white/50 min-h-10 flex items-center">
        Impossible de charger ·{' '}
        <button type="button" onClick={() => { refetch(); }} className="underline underline-offset-2 hover:text-white transition-colors ml-1">
          Réessayer
        </button>
      </p>
    );
  } else if (data.length === 0) {
    body = <p className="text-sm text-white/40 min-h-10 flex items-center">Aucun document</p>;
  } else {
    body = (
      <ul className="divide-y divide-white/5">
        {data.map((doc) => (
          <li key={doc.id}>
            <button
              type="button"
              onClick={() => open(doc.id, doc.originalFileName)}
              disabled={pendingId === doc.id}
              className="w-full text-left min-h-11 py-2 rounded-lg hover:bg-white/5 active:bg-white/10 disabled:opacity-60 transition-colors"
            >
              <span className="block text-sm text-white break-words">{doc.originalFileName}</span>
              <span className="block text-xs text-white/50">
                {DOCUMENT_KIND_LABELS[doc.kind] ?? doc.kind} · {formatBytes(doc.sizeBytes)} · {formatInstantDay(doc.createdAt)}
                {pendingId === doc.id && ' · ouverture…'}
              </span>
            </button>
          </li>
        ))}
      </ul>
    );
  }

  return (
    <section className="mt-5 pt-4 border-t border-white/10" aria-labelledby="echeance-documents-title">
      <h4 id="echeance-documents-title" className="text-sm font-semibold text-white/60 mb-1">Documents</h4>
      {body}
      {openError && <p className="text-xs text-amber-300/90 mt-1 break-words">{openError}</p>}

      <div className="mt-2">
        {adding ? (
          <DocumentUpload
            dashboardId={dashboardId}
            echeanceId={echeanceId}
            onUploaded={() => setAdding(false)}
            onOpenExisting={open}
            onCancel={() => setAdding(false)}
          />
        ) : (
          <button
            type="button"
            onClick={() => setAdding(true)}
            className="min-h-11 text-sm text-amber-300/90 hover:text-amber-200 transition-colors"
          >
            Ajouter un document
          </button>
        )}
      </div>

      {viewer && <DocumentViewer target={viewer} onClose={closeViewer} />}
    </section>
  );
};
