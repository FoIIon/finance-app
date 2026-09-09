import type { AgendaItem, AgendaUpcoming } from '../../types/agenda';
import { AgendaItemRow } from './AgendaItemRow';
import { formatDayHeading, formatShortDate } from './agendaFormat';

interface Props {
  upcoming: AgendaUpcoming;
  onOpenEcheance: (echeanceId: number) => void;
}

interface Group {
  date: string;
  items: AgendaItem[];
}

/** Regroupe par date dans l'ordre reçu : le serveur a déjà trié, retards portés en tête. */
const groupByDate = (items: AgendaItem[]): Group[] => {
  const groups: Group[] = [];
  for (const item of items) {
    const last = groups[groups.length - 1];
    if (last && last.date === item.date) last.items.push(item);
    else groups.push({ date: item.date, items: [item] });
  }
  return groups;
};

/** La vue « qu'est-ce qui m'attend », indépendante de la vue du haut : trente jours glissants, sans routine. */
export const AgendaUpcomingList = ({ upcoming, onOpenEcheance }: Props) => {
  const groups = groupByDate(upcoming.items);
  return (
    <section className="pt-6 mt-2 border-t border-white/10">
      <h3 className="text-lg font-bold text-white" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
        Les 30 prochains jours
      </h3>
      <p className="text-xs text-white/40 mt-0.5">
        du {formatShortDate(upcoming.from)} au {formatShortDate(upcoming.to)}
      </p>

      {groups.length === 0 ? (
        <p className="text-sm text-white/40 py-4">Rien dans les 30 prochains jours</p>
      ) : (
        <div className="mt-3 space-y-3">
          {groups.map((g) => (
            <div key={g.date} className="rounded-2xl border border-white/10 bg-white/5 p-4">
              <h4 className="text-sm font-semibold text-white/80">{formatDayHeading(g.date, false)}</h4>
              <ul className="divide-y divide-white/5 mt-1">
                {g.items.map((item) => (
                  <AgendaItemRow key={`${item.id}:${item.date}`} item={item} onOpenEcheance={onOpenEcheance} />
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}
    </section>
  );
};
