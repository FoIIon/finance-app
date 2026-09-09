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

/** « Mercredi 9 septembre », ou « Aujourd'hui 9 septembre » pour le jour courant. */
export const formatDayHeading = (iso: string, isToday: boolean) => {
  const dayMonth = fmt(iso, { day: 'numeric', month: 'long' });
  if (isToday) return `Aujourd'hui ${dayMonth}`;
  return capitalize(`${fmt(iso, { weekday: 'long' })} ${dayMonth}`);
};

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

/** « Rien le 13 », « Rien du 13 au 15 », « Rien du 29 sept. au 2 oct. » */
export const formatEmptyRange = (from: string, to: string) => {
  if (from === to) return `Rien le ${parseDay(from).getDate()}`;
  const a = parseDay(from);
  const b = parseDay(to);
  if (a.getMonth() === b.getMonth()) return `Rien du ${a.getDate()} au ${b.getDate()}`;
  return `Rien du ${formatShortDate(from)} au ${formatShortDate(to)}`;
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
