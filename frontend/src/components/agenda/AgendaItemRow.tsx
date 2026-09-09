import type { AgendaItem } from '../../types/agenda';
import { formatCurrency } from '../../utils/format';
import { formatTime, statusLabel } from './agendaFormat';

interface Props {
  item: AgendaItem;
  /** Fourni : une échéance se touche et ouvre la feuille basse. Une récurrente ne s'ouvre jamais. */
  onOpenEcheance?: (echeanceId: number) => void;
}

/**
 * Deux gabarits, aucune légende, aucune icône de type. Événement et manquant : l'heure à gauche, le
 * titre au centre. Échéance et récurrente : le titre au centre, le montant et le mot de statut à droite.
 * Le statut est écrit, la couleur ne porte rien seule : le rouge n'apparaît que sur « en retard ».
 */
export const AgendaItemRow = ({ item, onOpenEcheance }: Props) => {
  if (item.kind === 'event' || item.kind === 'missing') {
    const missing = item.kind === 'missing';
    const when = item.isAllDay ? 'Journée' : item.start ? formatTime(item.start) : '';
    return (
      <li className="flex gap-3 py-2">
        <span className="w-14 shrink-0 text-sm text-white/50 tabular-nums pt-px">{when}</span>
        <span className="min-w-0 flex-1">
          <span className={`block text-sm break-words ${missing ? 'italic text-white/50' : 'text-white'}`}>{item.title}</span>
          {item.location && <span className="block text-xs text-white/50 break-words mt-0.5">{item.location}</span>}
        </span>
      </li>
    );
  }

  const late = item.status === 'late';
  const content = (
    <>
      <span className="w-14 shrink-0" aria-hidden="true" />
      <span className="min-w-0 flex-1 text-sm text-white break-words">{item.title}</span>
      <span className="shrink-0 text-right">
        <span className="block text-sm font-semibold text-white tabular-nums">
          {item.amount != null ? formatCurrency(item.amount) : <span className="text-white/40 font-normal">montant inconnu</span>}
        </span>
        <span className={`block text-xs ${late ? 'text-red-400' : 'text-white/50'}`}>{statusLabel(item)}</span>
      </span>
    </>
  );

  if (item.kind === 'echeance' && item.echeanceId != null && onOpenEcheance) {
    const echeanceId = item.echeanceId;
    return (
      <li>
        <button
          type="button"
          onClick={() => onOpenEcheance(echeanceId)}
          className="w-full flex gap-3 py-2 text-left rounded-lg hover:bg-white/5 active:bg-white/10 transition-colors"
        >
          {content}
        </button>
      </li>
    );
  }

  return <li className="flex gap-3 py-2">{content}</li>;
};
