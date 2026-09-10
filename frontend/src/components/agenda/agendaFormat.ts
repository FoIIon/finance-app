// Mise en forme des dates et des mots de statut de l'agenda, en français. Rien ici ne décide
// d'un statut, d'un tri ou d'un retard : ces règles arrivent du serveur, on ne fait que les écrire.
import type { AgendaItem, AgendaView } from '../../types/agenda';

/** yyyy-MM-dd → Date locale à midi, sans décalage de fuseau à l'affichage. */
export const parseDay = (iso: string): Date => {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d, 12);
};

const capitalize = (s: string) => (s ? s.charAt(0).toUpperCase() + s.slice(1) : s);

const fmt = (iso: string, options: Intl.DateTimeFormatOptions) =>
  new Intl.DateTimeFormat('fr-FR', options).format(parseDay(iso));

/** « 8 sept. » */
export const formatShortDate = (iso: string) => fmt(iso, { day: 'numeric', month: 'short' });

/** « 15 septembre 2026 » */
export const formatLongDate = (iso: string) => fmt(iso, { day: 'numeric', month: 'long', year: 'numeric' });

/**
 * En-tête compact d'un jour : « Vendredi 11 », « Vendredi 11 septembre » quand le bandeau ne porte pas le
 * mois de ce jour, « Aujourd'hui · jeudi 10 » pour le jour courant.
 */
export const formatDayHeading = (iso: string, isToday: boolean, withMonth = false) => {
  const body = withMonth
    ? fmt(iso, { weekday: 'long', day: 'numeric', month: 'long' })
    : fmt(iso, { weekday: 'long', day: 'numeric' });
  return isToday ? `Aujourd'hui · ${body}` : capitalize(body);
};

/** « jeu. 10 », en tête d'une colonne de la semaine. */
export const formatColumnHeading = (iso: string) => fmt(iso, { weekday: 'short', day: 'numeric' });

/** Vrai si les deux dates yyyy-MM-dd sont dans le même mois : le titre du bandeau suffit alors. */
export const sameMonth = (a: string, b: string) => a.slice(0, 7) === b.slice(0, 7);

/** Lundi = 0 … dimanche = 6, pour placer une date dans une grille qui commence le lundi. Mise en page, rien d'autre. */
export const weekdayMondayFirst = (iso: string) => (parseDay(iso).getDay() + 6) % 7;

/** Titre du bandeau : « 9 au 15 septembre », « 28 septembre au 4 octobre », « Septembre 2026 ». */
export const formatPeriodTitle = (view: AgendaView, from: string, to: string) => {
  if (view === 'month') return capitalize(fmt(from, { month: 'long', year: 'numeric' }));
  const a = parseDay(from);
  const b = parseDay(to);
  if (a.getFullYear() !== b.getFullYear()) {
    return `${fmt(from, { day: 'numeric', month: 'long', year: 'numeric' })} au ${fmt(to, { day: 'numeric', month: 'long', year: 'numeric' })}`;
  }
  if (a.getMonth() !== b.getMonth()) {
    return `${fmt(from, { day: 'numeric', month: 'long' })} au ${fmt(to, { day: 'numeric', month: 'long' })}`;
  }
  return `${a.getDate()} au ${fmt(to, { day: 'numeric', month: 'long' })}`;
};

/** « 16:45 » → « 16h45 ». */
export const formatTime = (hhmm: string) => hhmm.replace(':', 'h');

/** « à l'instant », « il y a 12 min », « il y a 3 h », « il y a 2 j ». */
export const formatRelative = (iso: string, now: Date = new Date()) => {
  const diffMs = now.getTime() - new Date(iso).getTime();
  const minutes = Math.floor(diffMs / 60_000);
  if (minutes < 1) return "à l'instant";
  if (minutes < 60) return `il y a ${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `il y a ${hours} h`;
  const days = Math.floor(hours / 24);
  return `il y a ${days} j`;
};

/** « le 8 sept. » à partir d'un instant ISO UTC, dans le fuseau du navigateur. */
export const formatInstantDay = (iso: string) =>
  new Intl.DateTimeFormat('fr-FR', { day: 'numeric', month: 'short' }).format(new Date(iso));

/** Le mot de statut sous le montant. Le statut vient du serveur, on ne le déduit pas d'une date. */
export const statusLabel = (item: AgendaItem): string => {
  switch (item.status) {
    case 'due':
      return 'à payer';
    case 'late':
      return item.originalDate ? `en retard depuis le ${formatShortDate(item.originalDate)}` : 'en retard';
    case 'paid':
      return 'payée';
    case 'planned':
      return 'prévu';
    default:
      return '';
  }
};

/** Arithmétique de navigation sur yyyy-MM-dd. */
const toIso = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

export const addDays = (iso: string, n: number) => {
  const d = parseDay(iso);
  d.setDate(d.getDate() + n);
  return toIso(d);
};

export const addMonthsFirstDay = (iso: string, n: number) => {
  const d = parseDay(iso);
  return toIso(new Date(d.getFullYear(), d.getMonth() + n, 1, 12));
};

/** Les sept en-têtes de colonne d'une grille, du lundi au dimanche : « lun. », « mar. », … Le 1er janvier 2024 était un lundi. */
export const WEEKDAY_SHORT_LABELS: readonly string[] = [0, 1, 2, 3, 4, 5, 6].map((i) => fmt(addDays('2024-01-01', i), { weekday: 'short' }));
