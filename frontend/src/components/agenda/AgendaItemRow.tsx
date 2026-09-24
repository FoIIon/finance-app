import type { AgendaItem } from '../../types/agenda';
import { formatCurrency } from '../../utils/format';
import { formatTime, statusLabel } from './agendaFormat';

export type AgendaRowLayout = 'row' | 'column';

interface Props {
  item: AgendaItem;
  /** Fourni : une échéance se touche et ouvre la feuille basse. */
  onOpenEcheance?: (echeanceId: number) => void;
  /** Fourni : une récurrente se touche et ouvre la fiche Routine, réglée ou prévue. */
  onOpenRecurring?: (item: AgendaItem) => void;
  /**
   * `row` (défaut) : une seule ligne, l'heure à gauche pour un événement, le montant collé au titre, le
   * statut après. Si la ligne ne tient pas, seul le statut passe dessous. `column` : dans une colonne de la
   * semaine sur bureau, le titre sur une ou deux lignes puis le montant et le statut en dessous.
   */
  layout?: AgendaRowLayout;
}

/**
 * Deux gabarits, aucune légende, aucune icône de type. Le statut est écrit, la couleur ne porte rien
 * seule : le rouge n'apparaît que sur « en retard », le vert que sur « payée » et « réglée le ». Un titre ne se tronque jamais, il passe à la ligne.
 */
export const AgendaItemRow = ({ item, onOpenEcheance, onOpenRecurring, layout = 'row' }: Props) => {
  const column = layout === 'column';

  if (item.kind === 'event' || item.kind === 'missing') {
    const missing = item.kind === 'missing';
    const when = item.isAllDay ? 'Journée' : item.start ? formatTime(item.start) : '';
    const titleClass = missing ? 'italic text-white/50' : 'text-white';
    if (column) {
      return (
        <li className="min-h-10 py-1.5 text-sm break-words">
          {when && <span className="text-white/50 tabular-nums mr-1.5">{when}</span>}
          <span className={titleClass}>{item.title}</span>
          {item.location && <span className="block text-xs text-white/50">{item.location}</span>}
        </li>
      );
    }
    return (
      <li className="min-h-10 flex items-start gap-3 py-2">
        <span className="w-12 shrink-0 text-sm text-white/50 tabular-nums">{when}</span>
        <span className="min-w-0 flex-1">
          <span className={`block text-sm break-words ${titleClass}`}>{item.title}</span>
          {item.location && <span className="block text-xs text-white/50 break-words">{item.location}</span>}
        </span>
      </li>
    );
  }

  const late = item.status === 'late';
  const status = statusLabel(item);
  const amount =
    item.amount != null ? (
      <span className="whitespace-nowrap font-semibold tabular-nums text-white">{formatCurrency(item.amount)}</span>
    ) : (
      <span className="whitespace-nowrap text-white/40">montant inconnu</span>
    );
  const statusClass = `text-xs ${late ? 'text-red-400' : item.status === 'paid' ? 'text-emerald-400' : 'text-white/50'}`;

  const content = column ? (
    <>
      <span className="block text-sm text-white break-words">{item.title}</span>
      <span className="flex flex-wrap items-baseline gap-x-2 text-sm">
        {amount}
        {status && <span className={statusClass}>{status}</span>}
      </span>
    </>
  ) : (
    <>
      <span className="w-12 shrink-0" aria-hidden="true" />
      <span className="min-w-0 flex-1 flex flex-wrap items-baseline gap-x-2">
        {/* Espace insécable plus marge : 8 px, et le montant reste collé au dernier mot du titre, il ne passe jamais seul à la ligne. */}
        <span className="text-sm text-white break-words">
          {item.title}
          {' '}
          <span className="ml-1">{amount}</span>
        </span>
        {status && <span className={statusClass}>· {status}</span>}
      </span>
    </>
  );

  const rowClass = column ? 'min-h-10 flex flex-col gap-0.5 py-1.5' : 'min-h-10 flex items-start gap-3 py-2';
  const buttonClass = `w-full text-left rounded-lg hover:bg-white/5 active:bg-white/10 transition-colors ${rowClass}`;

  if (item.kind === 'echeance' && item.echeanceId != null && onOpenEcheance) {
    const echeanceId = item.echeanceId;
    return (
      <li>
        <button type="button" onClick={() => onOpenEcheance(echeanceId)} className={buttonClass}>
          {content}
        </button>
      </li>
    );
  }

  if (item.kind === 'recurring' && onOpenRecurring) {
    return (
      <li>
        <button type="button" onClick={() => onOpenRecurring(item)} className={buttonClass}>
          {content}
        </button>
      </li>
    );
  }

  return <li className={rowClass}>{content}</li>;
};
