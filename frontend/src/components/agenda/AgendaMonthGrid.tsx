import type { AgendaDay, AgendaItem } from '../../types/agenda';
import { WEEKDAY_SHORT_LABELS, addDays, formatDayMonth, parseDay, weekdayMondayFirst } from './agendaFormat';

interface Props {
  from: string;
  to: string;
  /** Les jours de la fenêtre que le serveur a rendus. Une date absente est un jour sans rien. */
  days: AgendaDay[];
  /** Le jour dont le détail est affiché sous la grille. */
  selected: string;
  onSelect: (iso: string) => void;
}

/** Au plus trois pastilles ou titres par case, le reste en « +n ». */
const MAX_PER_CELL = 3;

const dotClass = (item: AgendaItem) =>
  item.status === 'late' ? 'bg-red-400' : item.status === 'paid' ? 'bg-emerald-400' : 'bg-white/50';

/**
 * Ce qu'un lecteur d'écran lit sur une case, en mots : « 13 septembre, 2 items », « 10 septembre, aujourd'hui,
 * 1 en retard », « 14 septembre, rien ». Le statut vient du serveur, on le compte, on ne le déduit pas.
 */
const cellLabel = (iso: string, isToday: boolean, items: AgendaItem[]) => {
  const parts = [formatDayMonth(iso)];
  if (isToday) parts.push("aujourd'hui");
  const late = items.filter((i) => i.status === 'late').length;
  const others = items.length - late;
  if (items.length === 0) parts.push('rien');
  if (others > 0) parts.push(`${others} item${others > 1 ? 's' : ''}`);
  if (late > 0) parts.push(`${late} en retard`);
  return parts.join(', ');
};

/**
 * Les cases de la grille : les blancs avant le premier du mois, chaque jour de `from` à `to`, les blancs
 * qui complètent la dernière ligne. Placer une date dans une grille est de la mise en page, rien de plus.
 */
const buildCells = (from: string, to: string): (string | null)[] => {
  const cells: (string | null)[] = Array.from({ length: weekdayMondayFirst(from) }, () => null);
  for (let day = from; day <= to; day = addDays(day, 1)) cells.push(day);
  while (cells.length % 7 !== 0) cells.push(null);
  return cells;
};

/**
 * Le mois en grille, lundi en première colonne, téléphone comme bureau. Une case : le numéro, puis des
 * pastilles (téléphone) ou les titres coupés à deux lignes (bureau). Le détail du jour touché vit sous la
 * grille, dans le composant parent. Aujourd'hui : numéro dans un disque ambre. Le jour sélectionné : une
 * bordure ambre fine.
 */
export const AgendaMonthGrid = ({ from, to, days, selected, onSelect }: Props) => {
  const byDate = new Map(days.map((d) => [d.date, d]));
  const cells = buildCells(from, to);

  return (
    <div>
      <div className="grid grid-cols-7 gap-px lg:gap-1">
        {WEEKDAY_SHORT_LABELS.map((label) => (
          <div key={label} className="py-1 text-center text-[10px] lg:text-xs text-white/40">
            {label}
          </div>
        ))}
      </div>
      <div className="grid grid-cols-7 gap-px lg:gap-1" role="group" aria-label="Jours du mois">
        {cells.map((iso, index) => {
          if (iso == null) {
            return <div key={`blank:${index}`} aria-hidden="true" className="min-h-11 lg:min-h-24 rounded-md bg-white/2" />;
          }
          const day = byDate.get(iso);
          const items = day?.items ?? [];
          const isToday = day?.isToday ?? false;
          const isSelected = iso === selected;
          const overflow = items.length - MAX_PER_CELL;
          return (
            <button
              key={iso}
              type="button"
              onClick={() => onSelect(iso)}
              aria-pressed={isSelected}
              aria-label={cellLabel(iso, isToday, items)}
              className={`min-w-0 min-h-11 lg:min-h-24 flex flex-col items-start p-1 lg:p-1.5 rounded-md border text-left transition-colors hover:bg-white/5 ${
                isSelected ? 'border-amber-400/70' : 'border-transparent'
              } ${isToday ? 'bg-amber-500/5' : ''}`}
            >
              <span
                className={`text-xs tabular-nums leading-none ${
                  isToday
                    ? 'inline-flex h-5 w-5 items-center justify-center rounded-full bg-amber-400 font-bold text-[#0a0a1a]'
                    : 'text-white/70'
                }`}
              >
                {parseDay(iso).getDate()}
              </span>

              {items.length > 0 && (
                <>
                  <span className="mt-1 flex items-center gap-0.5 lg:hidden" aria-hidden="true">
                    {items.slice(0, MAX_PER_CELL).map((item) => (
                      <span key={`${item.id}:${item.date}`} className={`h-1.5 w-1.5 rounded-full ${dotClass(item)}`} />
                    ))}
                    {overflow > 0 && <span className="text-[10px] leading-none text-white/50">+{overflow}</span>}
                  </span>
                  <span className="mt-1 hidden w-full flex-col gap-0.5 lg:flex" aria-hidden="true">
                    {items.slice(0, MAX_PER_CELL).map((item) => (
                      <span key={`${item.id}:${item.date}`} className="flex min-w-0 items-start gap-1 text-xs leading-4 text-white/80">
                        <span className={`mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full ${dotClass(item)}`} />
                        <span className="min-w-0 line-clamp-2 break-words">{item.title}</span>
                      </span>
                    ))}
                    {overflow > 0 && <span className="text-[10px] text-white/50">+{overflow}</span>}
                  </span>
                </>
              )}
            </button>
          );
        })}
      </div>
    </div>
  );
};
