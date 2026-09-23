import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { documentsApi } from '../../api/documents';
import type { Document } from '../../types/documents';
import type { Echeance } from '../../types/agenda';
import { formatInstantDay } from '../agenda/agendaFormat';
import { DOCUMENT_KIND_LABELS, echeanceStatusView, formatBytes } from './documentFormat';

interface Props {
  doc: Document;
  dashboardId: number;
  /** L'échéance rattachée, quand la liste des échéances l'a donnée : son libellé et son statut. */
  echeance: Echeance | undefined;
  onOpen: (id: number, name: string) => void;
  opening: boolean;
  onOpenEcheance: (echeanceId: number) => void;
}

/**
 * Une carte, jamais une ligne de tableau : le nom, puis nature, taille lisible et date de dépôt. Toucher
 * le haut ouvre le document. En pied : « → Danse Alice · payée » si rattaché (ouvre la feuille Échéance),
 * le statut aux couleurs de l'agenda, et « Supprimer » en deux gestes. Payée : la bordure passe au vert, pour
 * voir d'un coup d'œil ce qui est réglé sans lire chaque carte.
 */
export const DocumentCard = ({ doc, dashboardId, echeance, onOpen, opening, onOpenEcheance }: Props) => {
  const queryClient = useQueryClient();
  const [deleteAsked, setDeleteAsked] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const remove = useMutation({
    mutationFn: () => documentsApi.remove(doc.id),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['documents', dashboardId] }),
        doc.echeanceId != null ? queryClient.invalidateQueries({ queryKey: ['echeance', doc.echeanceId] }) : Promise.resolve(),
      ]);
    },
    onError: () => { setError('Suppression impossible, réessaie.'); setDeleteAsked(false); },
  });

  const status = echeanceStatusView(echeance?.status);
  const paid = echeance?.status === 'Payee';

  return (
    <li className={`rounded-xl border ${paid ? 'border-emerald-400/40' : 'border-white/10'} bg-white/5 p-3 flex flex-col`}>
      <button
        type="button"
        onClick={() => onOpen(doc.id, doc.originalFileName)}
        disabled={opening || remove.isPending}
        className="w-full text-left min-h-11 rounded-lg -m-1 p-1 hover:bg-white/5 active:bg-white/10 disabled:opacity-60 transition-colors"
      >
        <span className="block text-sm font-medium text-white break-words">{doc.originalFileName}</span>
        <span className="block text-xs text-white/50 mt-0.5">
          {DOCUMENT_KIND_LABELS[doc.kind] ?? doc.kind}
          {doc.source === 'Mail' && (
            <span className="ml-1.5 px-1.5 py-px rounded-full bg-white/10 text-white/60 text-[10px] align-middle whitespace-nowrap">Reçu par mail</span>
          )}
          {' '}· {formatBytes(doc.sizeBytes)} · déposé le {formatInstantDay(doc.createdAt)}
          {opening && ' · ouverture…'}
        </span>
      </button>

      {error && <p className="text-xs text-amber-300/90 mt-1">{error}</p>}

      <div className="mt-2 flex items-center justify-between gap-2 flex-wrap text-xs">
        {doc.echeanceId != null ? (
          <button
            type="button"
            onClick={() => onOpenEcheance(doc.echeanceId!)}
            className="min-h-9 px-1 text-amber-300/90 hover:text-amber-200 text-left break-words transition-colors"
          >
            → {echeance?.label ?? 'Échéance'}
            {status && <span className={status.className}> · {status.label}</span>}
          </button>
        ) : (
          <span className="text-white/30 min-h-9 inline-flex items-center px-1">Sans échéance</span>
        )}

        {deleteAsked ? (
          <span className="flex items-center gap-2 text-white/70">
            <span>Supprimer ?</span>
            <button
              type="button"
              onClick={() => remove.mutate()}
              disabled={remove.isPending}
              className="min-h-9 px-3 rounded-lg bg-white/10 text-white hover:bg-white/15 disabled:opacity-50 transition-colors"
            >
              {remove.isPending ? '…' : 'Oui'}
            </button>
            <button
              type="button"
              onClick={() => setDeleteAsked(false)}
              disabled={remove.isPending}
              className="min-h-9 px-2 rounded-lg text-white/60 hover:text-white transition-colors"
            >
              Non
            </button>
          </span>
        ) : (
          <button
            type="button"
            onClick={() => setDeleteAsked(true)}
            className="min-h-9 px-1 text-white/40 hover:text-white/70 transition-colors"
          >
            Supprimer
          </button>
        )}
      </div>
    </li>
  );
};
