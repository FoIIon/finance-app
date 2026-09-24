import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { agendaApi } from '../../api/agenda';
import { useRecurringCandidatesQuery, useRecurringQuery } from '../../hooks/queries';
import { useToast } from '../../hooks/useToast';
import type { AgendaItem, RecurringCandidate } from '../../types/agenda';
import { formatCurrency } from '../../utils/format';
import { Sheet } from '../echeances/Sheet';
import { formatLongDate, formatShortDate, recurringIdOf, statusLabel } from './agendaFormat';

interface Props {
  /** La ligne telle que l'agenda la montre, rafraîchie après chaque geste. */
  item: AgendaItem;
  dashboardId: number;
  onClose: () => void;
}

const capitalize = (s: string) => (s ? s.charAt(0).toUpperCase() + s.slice(1) : s);

/** Le libellé d'une transaction sur une ligne : description, puis la contrepartie si elle ajoute quelque chose. */
const labelOf = (c: RecurringCandidate) =>
  [c.description, c.counterpartyName].filter((s) => s && s.trim()).join(' · ');

/**
 * Fiche Routine, sur le modèle d'EcheanceSheet : titre, montant prévu, jour théorique, statut. Réglée : la
 * transaction reconnue et « Ce n'est pas celle-ci » quand le lien est écrit en base (confirmation en ligne).
 * Reconnue d'après le montant ou le libellé, sans lien : on le dit, et la liste des candidats reste là pour
 * désigner la bonne, le lien l'emportant sur la reconnaissance. Pas réglée : la liste des transactions du
 * mois, « C'est celle-ci » pose le lien. Chaque geste invalide l'agenda et la liste, le serveur recalcule.
 * Une récurrente provisionnée en début de mois ne se lie ni ne se délie ici (le serveur répond 409) : la fiche
 * le dit et ne propose aucun geste, le rapprochement passe par la provision.
 * Rien n'est décidé ici : le statut, le montant réel et la date réelle arrivent du serveur.
 */
export const RecurringSheet = ({ item, dashboardId, onClose }: Props) => {
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const recurringId = recurringIdOf(item) ?? undefined;
  const month = item.date.slice(0, 7);
  const { data: candidates, isLoading: candidatesLoading, isError: candidatesError } = useRecurringCandidatesQuery(dashboardId, recurringId, month);
  const { data: recurring } = useRecurringQuery(dashboardId, recurringId);
  const [unlinkAsked, setUnlinkAsked] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const paid = item.status === 'paid';
  const provisioned = recurring?.provisionAtMonthStart ?? false;
  const settled = paid && item.transactionId != null ? candidates?.find((c) => c.id === item.transactionId) ?? null : null;
  const settledLinked = settled?.linkedToThisRecurring ?? false;
  const plannedAmount = recurring?.amount ?? (paid ? null : item.amount);

  const invalidate = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['agenda', dashboardId] }),
      queryClient.invalidateQueries({ queryKey: ['recurring-candidates', dashboardId, recurringId] }),
    ]);
  };

  const link = useMutation({
    mutationFn: (transactionId: number) => agendaApi.linkRecurring(recurringId!, { dashboardId, transactionId }),
    onSuccess: async (res, transactionId) => {
      setError(null);
      const when = res.data?.originalDate ?? candidates?.find((c) => c.id === transactionId)?.date ?? null;
      showToast(when ? `Réglée le ${formatShortDate(when)}` : 'Réglée', 'success');
      await invalidate();
    },
    onError: async (err) => {
      if (isAxiosError(err) && err.response?.status === 409) {
        showToast('Cette transaction règle déjà autre chose', 'warning');
        await invalidate();
        return;
      }
      setError("Impossible d'enregistrer le lien, réessaie.");
    },
  });

  const unlink = useMutation({
    mutationFn: (transactionId: number) => agendaApi.unlinkRecurring(recurringId!, dashboardId, transactionId),
    onSuccess: async () => {
      setError(null);
      setUnlinkAsked(false);
      showToast('Lien retiré', 'success');
      await invalidate();
    },
    onError: async (err) => {
      setUnlinkAsked(false);
      // 404 : le lien n'existait plus, l'écran était en retard sur le serveur.
      if (isAxiosError(err) && err.response?.status === 404) {
        await invalidate();
        return;
      }
      setError('Impossible de retirer le lien, réessaie.');
    },
  });

  const busy = link.isPending || unlink.isPending;
  const titleId = 'recurring-sheet-title';

  const candidateList = (
    <>
      {candidatesLoading && <p className="text-sm text-white/40">…</p>}
      {candidatesError && <p className="text-xs text-amber-300/90">Impossible de charger les transactions du mois.</p>}
      {candidates && candidates.length === 0 && <p className="text-sm text-white/50">Rien ce mois-ci sur les comptes du ménage</p>}
      {candidates && candidates.length > 0 && (
        <ul className="divide-y divide-white/5">
          {candidates.map((c) => (
            <li key={c.id} className="flex items-center gap-3 py-2">
              <span className="min-w-0 flex-1">
                <span className="flex flex-wrap items-baseline gap-x-2 text-sm">
                  <span className="text-white/50 tabular-nums">{formatShortDate(c.date)}</span>
                  <span className="font-semibold tabular-nums text-white">{formatCurrency(c.amount)}</span>
                </span>
                <span className="block text-xs text-white/60 break-words">{labelOf(c)}</span>
              </span>
              <button
                type="button"
                onClick={() => link.mutate(c.id)}
                disabled={busy || (paid && c.id === item.transactionId)}
                aria-label={`C'est celle-ci : ${formatShortDate(c.date)}, ${formatCurrency(c.amount)}`}
                className="shrink-0 min-h-11 px-3 rounded-lg border border-white/10 text-xs text-white/70 hover:text-white hover:bg-white/5 disabled:opacity-50 transition-colors"
              >
                C'est celle-ci
              </button>
            </li>
          ))}
        </ul>
      )}
    </>
  );

  return (
    <Sheet titleId={titleId} title={item.title || 'Récurrente'} onClose={onClose}>
      <dl className="text-sm space-y-1 mb-5">
        <div className="flex gap-2">
          <dt className="text-white/40 shrink-0">Montant prévu</dt>
          <dd className="text-white/80 tabular-nums">{plannedAmount != null ? formatCurrency(plannedAmount) : '…'}</dd>
        </div>
        <div className="flex gap-2">
          <dt className="text-white/40 shrink-0">Jour théorique</dt>
          <dd className="text-white/80">{formatLongDate(item.date)}</dd>
        </div>
        <div className="flex gap-2">
          <dt className="text-white/40 shrink-0">Statut</dt>
          <dd className={`min-w-0 ${paid ? 'text-emerald-400' : 'text-white/50'}`}>{capitalize(statusLabel(item))}</dd>
        </div>
      </dl>

      {error && <p className="text-xs text-amber-300/90 mb-3">{error}</p>}

      {provisioned ? (
        <p className="text-sm text-white/60">Provisionnée en début de mois, rapprochée par la provision</p>
      ) : paid ? (
        <section aria-labelledby="recurring-settled-title" className="rounded-xl border border-white/10 bg-white/5 p-4">
          <h4 id="recurring-settled-title" className="text-sm font-semibold text-white mb-2">Transaction reconnue</h4>
          <p className="flex flex-wrap items-baseline gap-x-2 text-sm">
            {item.originalDate && <span className="text-white/50 tabular-nums">{formatShortDate(item.originalDate)}</span>}
            {item.amount != null && <span className="font-semibold tabular-nums text-white">{formatCurrency(item.amount)}</span>}
          </p>
          <p className="text-xs text-white/60 truncate">
            {settled ? labelOf(settled) : candidatesLoading ? '…' : `Transaction n° ${item.transactionId ?? '?'}`}
          </p>

          {settledLinked ? (
            unlinkAsked ? (
              <div className="mt-3 flex items-center gap-3 text-sm text-white/70 min-h-11">
                <span>Retirer le lien ?</span>
                <button
                  type="button"
                  onClick={() => item.transactionId != null && unlink.mutate(item.transactionId)}
                  disabled={busy}
                  className="min-h-11 px-3 rounded-lg bg-white/10 text-white hover:bg-white/15 disabled:opacity-50 transition-colors"
                >
                  {unlink.isPending ? '…' : 'Oui'}
                </button>
                <button
                  type="button"
                  onClick={() => setUnlinkAsked(false)}
                  disabled={busy}
                  className="min-h-11 px-3 rounded-lg text-white/60 hover:text-white transition-colors"
                >
                  Non
                </button>
              </div>
            ) : (
              <button
                type="button"
                onClick={() => setUnlinkAsked(true)}
                disabled={busy}
                className="mt-3 w-full min-h-11 rounded-xl border border-white/10 text-white/70 hover:text-white hover:bg-white/5 disabled:opacity-50 transition-colors"
              >
                Ce n'est pas celle-ci
              </button>
            )
          ) : (
            settled && (
              <>
                <p className="mt-3 text-xs text-white/50">Reconnue d'après le montant ou le libellé. Si ce n'est pas elle, désigne la bonne ci-dessous.</p>
                <div className="mt-2">{candidateList}</div>
              </>
            )
          )}
        </section>
      ) : (
        <section aria-labelledby="recurring-candidates-title">
          <h4 id="recurring-candidates-title" className="text-sm font-semibold text-white mb-1">Aucune transaction reconnue ce mois-ci</h4>
          <p className="text-xs text-white/50 mb-2">Désigne celle qui la règle, le lien servira les mois suivants.</p>
          {candidateList}
        </section>
      )}
    </Sheet>
  );
};
