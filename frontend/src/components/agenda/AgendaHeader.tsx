import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { bankingApi } from '../../api/banking';
import type { AgendaView, CalendarStatus } from '../../types/agenda';
import { CreateEcheanceButton } from '../echeances/CreateEcheanceButton';
import { formatInstantDay, formatRelative, formatSeenAgo, latestInstant } from './agendaFormat';

interface Props {
  view: AgendaView;
  onViewChange: (view: AgendaView) => void;
  onPrev: () => void;
  onNext: () => void;
  onToday: () => void;
  /** Vrai seulement quand la période affichée ne contient pas aujourd'hui. */
  showToday: boolean;
  /** Tant que le serveur n'a pas donné la fenêtre, les flèches n'ont pas de base. */
  navDisabled: boolean;
  title: string;
  calendar: CalendarStatus | undefined;
}

const SettingsLink = ({ label }: { label: string }) => (
  <Link to="/dashboard-settings" className="underline underline-offset-2 hover:text-white transition-colors">
    {label}
  </Link>
);

/**
 * L'état du calendrier en une ligne datée, gris ou ambre, jamais un bandeau rouge. « Vu il y a » se
 * calcule sur la dernière synchronisation réussie, une erreur se date sur la dernière tentative.
 */
const CalendarStatusLine = ({ calendar }: { calendar: CalendarStatus | undefined }) => {
  if (!calendar) return null;

  if (!calendar.connected) {
    return (
      <p className="text-xs text-white/50">
        Calendrier familial non connecté · <SettingsLink label="Connecter" />
      </p>
    );
  }

  switch (calendar.lastSyncStatus) {
    case 'KeyLost':
      return (
        <p className="text-xs text-amber-300/90">
          Calendrier : clé perdue, à ressaisir · <SettingsLink label="Paramètres" />
        </p>
      );
    case 'HttpError':
    case 'Invalid':
      return (
        <p className="text-xs text-amber-300/90">
          Calendrier : erreur de lecture{calendar.lastAttemptAt ? ` le ${formatInstantDay(calendar.lastAttemptAt)}` : ''} ·{' '}
          <SettingsLink label="Vérifier" />
        </p>
      );
    case 'Pending':
      return <p className="text-xs text-white/50">Calendrier : lecture en attente</p>;
    default:
      return (
        <p className="text-xs text-white/50">
          {calendar.lastSyncAt ? `Calendrier vu ${formatRelative(calendar.lastSyncAt)}` : 'Calendrier connecté'}
        </p>
      );
  }
};

/**
 * « Compte vu il y a 3 h » (lot 3) : la dernière synchronisation bancaire réussie, toutes connexions
 * confondues. C'est ce qui dit si le silence d'une échéance veut dire impayée ou pas encore synchronisée.
 * Aucune connexion : rien. La requête est la même que la page Comptes, en lecture seule.
 */
const BankSeenLine = () => {
  const { data } = useQuery({
    queryKey: ['bank-connections'],
    staleTime: 5 * 60 * 1000,
    queryFn: async () => (await bankingApi.getConnections()).data,
  });
  if (!data || data.length === 0) return null;
  const last = latestInstant(data.map((c) => c.lastSyncAt));
  return <p className="text-xs text-white/50">{last ? `Compte vu ${formatSeenAgo(last)}` : 'Compte pas encore vu'}</p>;
};

const VIEWS: { key: AgendaView; label: string }[] = [
  { key: 'week', label: 'Semaine' },
  { key: 'month', label: 'Mois' },
];

/**
 * Bandeau collant en deux lignes. Ligne 1 : la pilule Semaine | Mois (même famille que PeriodSelector),
 * « Aujourd'hui » hors période courante, les flèches en cible tactile de 44 px, « + Échéance » à droite
 * (lot 1). Ligne 2 : le titre de la période et l'état du calendrier. Le fond reprend celui de l'en-tête mobile pour que le contenu passe
 * dessous sans transparaître.
 */
export const AgendaHeader = ({ view, onViewChange, onPrev, onNext, onToday, showToday, navDisabled, title, calendar }: Props) => (
  <div className="sticky top-[60px] md:top-0 z-20 -mx-4 px-4 md:-mx-8 md:px-8 -mt-1 md:-mt-8 pt-2 md:pt-6 pb-3 bg-[#0a0a1a]/90 backdrop-blur-xl border-b border-white/5">
    <div className="flex items-center gap-2 flex-wrap">
      <div role="group" aria-label="Vue" className="flex gap-1 p-1 rounded-xl bg-white/5 border border-white/10">
        {VIEWS.map((v) => (
          <button
            key={v.key}
            type="button"
            aria-pressed={view === v.key}
            onClick={() => onViewChange(v.key)}
            className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-all ${
              view === v.key
                ? 'bg-amber-500/20 text-amber-300 border border-amber-500/30'
                : 'text-white/60 hover:text-white hover:bg-white/5 border border-transparent'
            }`}
          >
            {v.label}
          </button>
        ))}
      </div>

      <div className="ml-auto flex items-center gap-1">
        {showToday && (
          <button
            type="button"
            onClick={onToday}
            className="min-h-11 px-3 rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-300 text-sm font-medium hover:bg-amber-500/20 transition-colors"
          >
            Aujourd'hui
          </button>
        )}
        <button
          type="button"
          onClick={onPrev}
          disabled={navDisabled}
          aria-label="Période précédente"
          className="min-w-11 min-h-11 rounded-xl border border-white/10 bg-white/5 text-white/70 text-xl leading-none hover:text-white hover:bg-white/10 disabled:opacity-40 transition-colors"
        >
          ‹
        </button>
        <button
          type="button"
          onClick={onNext}
          disabled={navDisabled}
          aria-label="Période suivante"
          className="min-w-11 min-h-11 rounded-xl border border-white/10 bg-white/5 text-white/70 text-xl leading-none hover:text-white hover:bg-white/10 disabled:opacity-40 transition-colors"
        >
          ›
        </button>
        <CreateEcheanceButton />
      </div>
    </div>

    <div className="mt-2 flex flex-col gap-0.5 md:flex-row md:items-baseline md:justify-between md:gap-4">
      <h2 className="text-lg md:text-2xl font-bold text-white min-h-7" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
        {title}
      </h2>
      <div className="flex flex-col gap-0.5 md:items-end">
        <CalendarStatusLine calendar={calendar} />
        <BankSeenLine />
      </div>
    </div>
  </div>
);
