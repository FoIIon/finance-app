import type { AgendaItem, AgendaUpcoming } from '../../types/agenda';
import { AgendaDayBlock } from './AgendaDayBlock';
import { formatShortDate } from './agendaFormat';

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

/**
 * « Et ensuite » : ce que la période affichée ne montre pas encore, jusqu'à trente jours. Le serveur a
 * déjà retiré ce que la période affiche. Vide, la section n'apparaît pas du tout.
 */
export const AgendaUpcomingList = ({ upcoming, onOpenEcheance }: Props) => {
  if (upcoming.items.length === 0) return null;
  const groups = groupByDate(upcoming.items);
  return (
    <section className="pt-6 mt-4 border-t border-white/10">
      <h3 className="text-lg font-bold text-white" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
        Et ensuite
      </h3>
      <p className="text-xs text-white/40 mt-0.5">jusqu'au {formatShortDate(upcoming.to)}</p>

      <div className="mt-2 -mx-2">
        {groups.map((g) => (
          <AgendaDayBlock
            key={g.date}
            day={{ date: g.date, isToday: false, items: g.items, routine: [] }}
            withMonth
            onOpenEcheance={onOpenEcheance}
          />
        ))}
      </div>
    </section>
  );
};
