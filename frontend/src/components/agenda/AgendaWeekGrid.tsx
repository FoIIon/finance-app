import type { Ref } from 'react';
import type { AgendaDay, AgendaItem } from '../../types/agenda';
import { AgendaDayBlock } from './AgendaDayBlock';
import { sameMonth } from './agendaFormat';
import { useIsDesktop } from './useIsDesktop';

interface Props {
  /** Les jours de la fenêtre, dans l'ordre du serveur. Le jour courant hors fenêtre n'en fait pas partie. */
  days: AgendaDay[];
  from: string;
  to: string;
  todayRef: Ref<HTMLElement>;
  onOpenEcheance: (echeanceId: number) => void;
  onOpenRecurring: (item: AgendaItem) => void;
}

/**
 * La semaine. Téléphone : une colonne de groupes compacts, un jour vide sur une ligne. Bureau (`lg:`) :
 * sept colonnes égales, même hauteur, rien ne défile de côté. Le même DOM sert les deux, seul le gabarit
 * des jours change avec la largeur.
 */
export const AgendaWeekGrid = ({ days, from, to, todayRef, onOpenEcheance, onOpenRecurring }: Props) => {
  const desktop = useIsDesktop();
  const withMonth = !sameMonth(from, to);
  return (
    <div className={desktop ? 'grid grid-cols-7 gap-1' : '-mx-2'}>
      {days.map((day) => (
        <AgendaDayBlock
          key={day.date}
          day={day}
          layout={desktop ? 'column' : 'row'}
          withMonth={withMonth}
          ref={day.isToday ? todayRef : undefined}
          onOpenEcheance={onOpenEcheance}
          onOpenRecurring={onOpenRecurring}
        />
      ))}
    </div>
  );
};
