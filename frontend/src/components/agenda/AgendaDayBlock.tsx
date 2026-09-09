import { useState, type Ref } from 'react';
import type { AgendaDay } from '../../types/agenda';
import { AgendaItemRow } from './AgendaItemRow';
import { formatDayHeading } from './agendaFormat';

interface Props {
  day: AgendaDay;
  onOpenEcheance: (echeanceId: number) => void;
  /** Posé sur le jour courant, pour que l'écran s'ouvre dessus. */
  ref?: Ref<HTMLElement>;
}

/**
 * Un bloc par entrée de `days`. Le jour courant a un filet gauche ambre et un fond teinté. La routine
 * (séries hebdomadaires, calculée par le serveur) est repliée par défaut, son état n'est pas persisté.
 */
export const AgendaDayBlock = ({ day, onOpenEcheance, ref }: Props) => {
  const [routineOpen, setRoutineOpen] = useState(false);
  const empty = day.items.length === 0 && day.routine.length === 0;

  return (
    <section
      ref={ref}
      className={`rounded-2xl border p-4 scroll-mt-36 md:scroll-mt-32 ${
        day.isToday
          ? 'border-white/10 border-l-2 border-l-amber-400 bg-amber-500/5'
          : 'border-white/10 bg-white/5'
      }`}
    >
      <h3 className={`text-sm font-semibold ${day.isToday ? 'text-amber-300' : 'text-white/80'}`}>
        {formatDayHeading(day.date, day.isToday)}
      </h3>

      {empty ? (
        <p className="text-sm text-white/40 py-2">Rien de prévu</p>
      ) : (
        <ul className="divide-y divide-white/5 mt-1">
          {day.items.map((item) => (
            <AgendaItemRow key={`${item.id}:${item.date}`} item={item} onOpenEcheance={onOpenEcheance} />
          ))}
        </ul>
      )}

      {day.routine.length > 0 && (
        <div className="mt-1 border-t border-white/5">
          <button
            type="button"
            aria-expanded={routineOpen}
            onClick={() => setRoutineOpen((o) => !o)}
            className="w-full min-h-11 flex items-center gap-2 text-left text-xs text-white/50 hover:text-white/80 transition-colors"
          >
            <span aria-hidden="true" className="w-3 text-center">{routineOpen ? '▾' : '▸'}</span>
            <span>Routine · {day.routine.length}</span>
          </button>
          {routineOpen && (
            <ul className="divide-y divide-white/5 pb-1">
              {day.routine.map((item) => (
                <AgendaItemRow key={`${item.id}:${item.date}`} item={item} />
              ))}
            </ul>
          )}
        </div>
      )}
    </section>
  );
};
