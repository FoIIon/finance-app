import { useState, type Ref } from 'react';
import type { AgendaDay, AgendaItem } from '../../types/agenda';
import { AgendaItemRow, type AgendaRowLayout } from './AgendaItemRow';
import { formatColumnHeading, formatDayHeading } from './agendaFormat';

interface Props {
  day: AgendaDay;
  onOpenEcheance?: (echeanceId: number) => void;
  onOpenRecurring?: (item: AgendaItem) => void;
  /** Posé sur le jour courant, pour que l'écran s'ouvre dessus. */
  ref?: Ref<HTMLElement>;
  /** Le mois dans l'en-tête, quand le titre du bandeau ne le porte pas pour ce jour. */
  withMonth?: boolean;
  /** `row` : un groupe d'une colonne (téléphone, détail du mois, à venir). `column` : une colonne de la semaine sur bureau. */
  layout?: AgendaRowLayout;
}

/**
 * Un jour sans carte : un en-tête d'une ligne, ses lignes collées dessous, un filet. Un jour vide tient
 * sur une seule ligne, sauf Aujourd'hui qui garde sa hauteur pleine. La routine (séries hebdomadaires,
 * calculée par le serveur) est repliée par défaut, son état n'est pas persisté.
 */
export const AgendaDayBlock = ({ day, onOpenEcheance, onOpenRecurring, ref, withMonth = false, layout = 'row' }: Props) => {
  const [routineOpen, setRoutineOpen] = useState(false);
  const empty = day.items.length === 0 && day.routine.length === 0;
  const column = layout === 'column';

  const items = (
    <ul className="divide-y divide-white/5">
      {day.items.map((item) => (
        <AgendaItemRow key={`${item.id}:${item.date}`} item={item} onOpenEcheance={onOpenEcheance} onOpenRecurring={onOpenRecurring} layout={layout} />
      ))}
    </ul>
  );

  const routine = day.routine.length > 0 && (
    <div className={`border-t border-white/5 ${column ? 'mt-auto' : ''}`}>
      <button
        type="button"
        aria-expanded={routineOpen}
        onClick={() => setRoutineOpen((o) => !o)}
        className="w-full min-h-10 flex items-center gap-2 text-left text-xs text-white/50 hover:text-white/80 transition-colors"
      >
        <span aria-hidden="true" className="w-3 text-center">{routineOpen ? '▾' : '▸'}</span>
        <span>Routine · {day.routine.length}</span>
      </button>
      {routineOpen && (
        <ul className="divide-y divide-white/5 pb-1">
          {day.routine.map((item) => (
            <AgendaItemRow key={`${item.id}:${item.date}`} item={item} layout={layout} />
          ))}
        </ul>
      )}
    </div>
  );

  if (column) {
    return (
      <section
        ref={ref}
        className={`min-w-0 flex flex-col rounded-lg px-1.5 pb-1 scroll-mt-32 ${day.isToday ? 'bg-amber-500/5' : ''}`}
      >
        <h3 className={`h-11 flex flex-col justify-end pb-1 text-xs font-semibold ${day.isToday ? 'text-amber-300' : 'text-white/60'}`}>
          {day.isToday && <span className="text-[10px] uppercase tracking-wider">Aujourd'hui</span>}
          <span>{formatColumnHeading(day.date)}</span>
        </h3>
        {empty ? (
          <p className="min-h-10 flex items-center text-sm text-white/30">
            <span aria-hidden="true">–</span>
            <span className="sr-only">Rien de prévu</span>
          </p>
        ) : (
          items
        )}
        {routine}
      </section>
    );
  }

  const heading = formatDayHeading(day.date, day.isToday, withMonth);
  const headingClass = 'text-sm [font-variant-caps:small-caps] tracking-wide';

  if (empty && !day.isToday) {
    return (
      <section ref={ref} className="h-8 flex items-center border-b border-white/5 border-l-2 border-l-transparent px-1.5 scroll-mt-36 md:scroll-mt-32">
        <h3 className={`${headingClass} font-medium text-white/40`}>{heading} · rien</h3>
      </section>
    );
  }

  return (
    <section
      ref={ref}
      className={`border-b border-white/5 border-l-2 px-1.5 scroll-mt-36 md:scroll-mt-32 ${
        day.isToday ? 'border-l-amber-400 bg-amber-500/5' : 'border-l-transparent'
      }`}
    >
      <h3 className={`h-7 flex items-center ${headingClass} font-semibold ${day.isToday ? 'text-amber-300' : 'text-white/50'}`}>{heading}</h3>
      {empty ? <p className="min-h-10 flex items-center text-sm text-white/40">Rien aujourd'hui</p> : items}
      {routine}
    </section>
  );
};
