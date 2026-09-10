import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { echeancesApi } from '../../api/agenda';
import { useEcheanceQuery } from '../../hooks/queries';
import type { AgendaItem, AgendaStatus } from '../../types/agenda';
import { formatCurrency } from '../../utils/format';
import { formatInstantDay, formatLongDate, statusLabel } from './agendaFormat';
import { EcheanceDocumentsSection } from '../echeances/EcheanceDocumentsSection';
import { EcheanceEditDelete } from '../echeances/EcheanceEditDelete';
import { EcheanceFormSheet } from '../echeances/EcheanceFormSheet';

interface Props {
  echeanceId: number;
  /** La ligne telle que l'agenda la montre, rafraîchie après chaque geste. Absente si elle a quitté l'écran. */
  item: AgendaItem | undefined;
  dashboardId: number;
  onClose: () => void;
}

/** Le texte du serveur quand il en donne un (400, 409), sinon une phrase neutre. */
const messageOf = (err: unknown, fallback: string) => {
  if (isAxiosError(err) && typeof err.response?.data === 'string' && err.response.data.trim()) return err.response.data;
  return fallback;
};

/** Le statut de l'EcheanceDto (AVenir, EnRetard, Payee) ramené aux mots de l'agenda, sans rien recalculer. */
const statusFromDto = (status: string | undefined): AgendaStatus | null => {
  switch (status) {
    case 'Payee':
      return 'paid';
    case 'EnRetard':
      return 'late';
    case 'AVenir':
      return 'due';
    default:
      return null;
  }
};

/**
 * Feuille basse sur le patron de CategoryDetailModal. Titre, montant, date limite, statut en texte, puis
 * les gestes : « Je l'ai payée », réversible au même endroit par « Finalement non », et « Détacher la
 * transaction » en deux gestes. Chaque geste invalide l'agenda, le serveur recalcule le statut. Lot 1 :
 * une section Documents, puis « Modifier » (la feuille de saisie prend la place de celle-ci) et « Supprimer ».
 */
export const EcheanceSheet = ({ echeanceId, item, dashboardId, onClose }: Props) => {
  const queryClient = useQueryClient();
  const { data: echeance, isLoading } = useEcheanceQuery(echeanceId);
  const [detachAsked, setDetachAsked] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);

  // Pendant la modification, la feuille de saisie tient Échap : sinon les deux se fermeraient d'un coup.
  useEffect(() => {
    if (editing) return;
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onEsc);
    return () => document.removeEventListener('keydown', onEsc);
  }, [onClose, editing]);

  const invalidate = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['agenda', dashboardId] }),
      queryClient.invalidateQueries({ queryKey: ['echeance', echeanceId] }),
    ]);
  };

  const pay = useMutation({
    mutationFn: () => echeancesApi.pay(echeanceId),
    onSuccess: () => { setError(null); return invalidate(); },
    // 409 « déjà payée » : l'écran était en retard sur le serveur, on se remet à jour.
    onError: (err) => { setError(messageOf(err, "Impossible d'enregistrer le paiement, réessaie.")); return invalidate(); },
  });

  const unpay = useMutation({
    mutationFn: () => echeancesApi.unpay(echeanceId),
    onSuccess: () => { setError(null); return invalidate(); },
    onError: (err) => setError(messageOf(err, "Impossible d'annuler le paiement, réessaie.")),
  });

  // PUT de remplacement complet : on renvoie les champs lus, seul transactionId passe à null.
  const detach = useMutation({
    mutationFn: () => {
      if (!echeance) throw new Error('Échéance non chargée');
      return echeancesApi.update(echeanceId, {
        label: echeance.label,
        dueDate: echeance.dueDate,
        amount: echeance.amount,
        notes: echeance.notes,
        transactionId: null,
      });
    },
    onSuccess: () => { setError(null); setDetachAsked(false); return invalidate(); },
    onError: (err) => setError(messageOf(err, 'Impossible de détacher la transaction, réessaie.')),
  });

  const busy = pay.isPending || unpay.isPending || detach.isPending;
  const status: AgendaStatus | null = item?.status ?? statusFromDto(echeance?.status);
  const title = item?.title ?? echeance?.label ?? '';
  const amount = item ? item.amount : echeance?.amount ?? null;
  const dueDate = echeance?.dueDate ?? (item?.originalDate ?? item?.date);
  const transactionId = echeance?.transactionId ?? item?.transactionId ?? null;

  const statusText = (() => {
    if (status === 'paid') {
      if (echeance?.paidAt) return `Payée le ${formatInstantDay(echeance.paidAt)}`;
      return transactionId != null ? 'Payée, réglée par une transaction' : 'Payée';
    }
    if (item) {
      const label = statusLabel(item);
      return label ? label.charAt(0).toUpperCase() + label.slice(1) : '';
    }
    if (status === 'late') return 'En retard';
    if (status === 'due') return 'À payer';
    return '';
  })();

  if (editing && echeance) {
    return <EcheanceFormSheet dashboardId={dashboardId} initial={echeance} onClose={() => setEditing(false)} />;
  }

  const sheet = (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="echeance-sheet-title"
      className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-end md:items-center justify-center"
      onClick={onClose}
    >
      <div
        className="bg-[#1a1a3e] rounded-t-2xl md:rounded-2xl border border-white/10 p-6 pb-[max(1.5rem,env(safe-area-inset-bottom))] w-full md:max-w-md max-h-[85vh] overflow-y-auto"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-3 mb-4">
          <div className="min-w-0">
            <h3 id="echeance-sheet-title" className="text-xl font-bold text-white break-words">{title || 'Échéance'}</h3>
            <p className="text-white/70 text-lg font-semibold tabular-nums mt-1">
              {amount != null ? formatCurrency(amount) : <span className="text-white/40 text-sm font-normal">Montant inconnu</span>}
            </p>
          </div>
          <button onClick={onClose} aria-label="Fermer" className="text-white/40 hover:text-white text-2xl leading-none px-2 min-h-11">
            ×
          </button>
        </div>

        <dl className="text-sm space-y-1 mb-5">
          <div className="flex gap-2">
            <dt className="text-white/40 shrink-0">Date limite</dt>
            <dd className="text-white/80">{dueDate ? formatLongDate(dueDate) : (isLoading ? '…' : '')}</dd>
          </div>
          <div className="flex gap-2">
            <dt className="text-white/40 shrink-0">Statut</dt>
            <dd className={status === 'late' ? 'text-red-400' : 'text-white/80'}>{statusText || (isLoading ? '…' : '')}</dd>
          </div>
        </dl>

        {error && <p className="text-xs text-amber-300/90 mb-3">{error}</p>}

        <div className="space-y-2">
          {(status === 'due' || status === 'late') && (
            <button
              type="button"
              onClick={() => pay.mutate()}
              disabled={busy}
              className="w-full min-h-11 rounded-xl bg-amber-500/20 text-amber-300 border border-amber-500/30 font-medium hover:bg-amber-500/30 disabled:opacity-50 transition-colors"
            >
              {pay.isPending ? 'Enregistrement…' : "Je l'ai payée"}
            </button>
          )}

          {status === 'paid' && (
            <button
              type="button"
              onClick={() => unpay.mutate()}
              disabled={busy}
              className="w-full min-h-11 rounded-xl border border-white/10 text-white/70 hover:text-white hover:bg-white/5 disabled:opacity-50 transition-colors"
            >
              {unpay.isPending ? 'Annulation…' : 'Finalement non'}
            </button>
          )}

          {transactionId != null && echeance && (
            detachAsked ? (
              <div className="flex items-center gap-3 text-sm text-white/70 min-h-11 px-1">
                <span>Détacher ?</span>
                <button
                  type="button"
                  onClick={() => detach.mutate()}
                  disabled={busy}
                  className="min-h-11 px-3 rounded-lg bg-white/10 text-white hover:bg-white/15 disabled:opacity-50 transition-colors"
                >
                  {detach.isPending ? '…' : 'Oui'}
                </button>
                <button
                  type="button"
                  onClick={() => setDetachAsked(false)}
                  disabled={busy}
                  className="min-h-11 px-3 rounded-lg text-white/60 hover:text-white transition-colors"
                >
                  Non
                </button>
              </div>
            ) : (
              <button
                type="button"
                onClick={() => setDetachAsked(true)}
                disabled={busy}
                className="w-full min-h-11 text-sm text-white/40 hover:text-white/70 transition-colors"
              >
                Détacher la transaction
              </button>
            )
          )}
        </div>

        <EcheanceDocumentsSection echeanceId={echeanceId} dashboardId={dashboardId} />

        {echeance && (
          <EcheanceEditDelete echeanceId={echeanceId} dashboardId={dashboardId} onEdit={() => setEditing(true)} onDeleted={onClose} />
        )}
      </div>
    </div>
  );

  return createPortal(sheet, document.body);
};
