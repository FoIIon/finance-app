import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useDashboards } from '../hooks/useDashboards';
import { useAgendaQuery } from '../hooks/queries';
import type { AgendaDay, AgendaItem, AgendaResult, AgendaView } from '../types/agenda';
import { AgendaHeader } from '../components/agenda/AgendaHeader';
import { AgendaDayBlock } from '../components/agenda/AgendaDayBlock';
import { AgendaWeekGrid } from '../components/agenda/AgendaWeekGrid';
import { AgendaMonthGrid } from '../components/agenda/AgendaMonthGrid';
import { AgendaUpcomingList } from '../components/agenda/AgendaUpcomingList';
import { AgendaSkeleton } from '../components/agenda/AgendaSkeleton';
import { EcheanceSheet } from '../components/agenda/EcheanceSheet';
import { RecurringSheet } from '../components/agenda/RecurringSheet';
import { addDays, addMonthsFirstDay, formatPeriodTitle } from '../components/agenda/agendaFormat';

const ISO_DAY = /^\d{4}-\d{2}-\d{2}$/;

/** L'échéance ouverte, relue dans les données fraîches après chaque geste. */
const findEcheance = (data: AgendaResult | undefined, echeanceId: number | null): AgendaItem | undefined => {
  if (!data || echeanceId == null) return undefined;
  const match = (i: AgendaItem) => i.kind === 'echeance' && i.echeanceId === echeanceId;
  for (const day of data.days) {
    const found = day.items.find(match);
    if (found) return found;
  }
  return data.upcoming.items.find(match);
};

/** La récurrente ouverte, par l'id de son occurrence (recurring:<id>:<date>), relue dans les données fraîches après chaque geste. */
const findRecurring = (data: AgendaResult | undefined, itemId: string | null): AgendaItem | undefined => {
  if (!data || itemId == null) return undefined;
  const match = (i: AgendaItem) => i.kind === 'recurring' && i.id === itemId;
  for (const day of data.days) {
    const found = day.items.find(match);
    if (found) return found;
  }
  return data.upcoming.items.find(match);
};

const inWindow = (iso: string, data: AgendaResult) => iso >= data.from && iso <= data.to;

/**
 * Le jour ouvert sous la grille du mois : celui de l'URL s'il est dans le mois, sinon aujourd'hui si le
 * mois le contient, sinon le premier jour du mois qui porte un item, sinon le premier du mois. C'est un
 * choix d'affichage, le serveur a déjà décidé de tout ce que les jours contiennent.
 */
const selectedDayOf = (data: AgendaResult, windowDays: AgendaDay[], dayParam: string | null): string => {
  if (dayParam && ISO_DAY.test(dayParam) && inWindow(dayParam, data)) return dayParam;
  if (inWindow(data.today, data)) return data.today;
  return windowDays.find((d) => d.items.length > 0)?.date ?? data.from;
};

const ErrorLine = ({ onRetry }: { onRetry: () => void }) => (
  <p className="text-sm text-white/50 py-4">
    Impossible de charger l'agenda ·{' '}
    <button type="button" onClick={onRetry} className="underline underline-offset-2 hover:text-white transition-colors">
      Réessayer
    </button>
  </p>
);

/**
 * La route /agenda : jour par jour, ce qui est à payer, ce qui est prévu et l'agenda de la famille, à la
 * semaine ou au mois. `view`, `anchor` et `day` vivent dans l'URL, le retour arrière fonctionne et un lien
 * se partage. Aucune règle ici : statut, tri, retards portés, à venir dédoublonné et routine arrivent du serveur.
 */
const Agenda = () => {
  const { currentDashboard } = useDashboards();
  const [params, setParams] = useSearchParams();
  const view: AgendaView = params.get('view') === 'month' ? 'month' : 'week';
  const anchorParam = params.get('anchor');
  const anchor = anchorParam && ISO_DAY.test(anchorParam) ? anchorParam : undefined;
  const dayParam = params.get('day');

  const dashboardId = currentDashboard?.id;
  const { data, isPending, isError, isPlaceholderData, refetch } = useAgendaQuery(dashboardId, view, anchor);

  const [openEcheanceId, setOpenEcheanceId] = useState<number | null>(null);
  const [openRecurringId, setOpenRecurringId] = useState<string | null>(null);
  const openRecurring = (item: AgendaItem) => setOpenRecurringId(item.id);

  // L'écran s'ouvre une seule fois sur le bloc d'arrivée (aujourd'hui en semaine, le détail du jour sous la
  // grille en mois), et seulement si ce bloc n'est pas déjà visible.
  const landingRef = useRef<HTMLElement>(null);
  const scrolledOnce = useRef(false);
  useEffect(() => {
    if (!data || scrolledOnce.current) return;
    scrolledOnce.current = true;
    const el = landingRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    if (rect.bottom > window.innerHeight) el.scrollIntoView({ block: 'start' });
  }, [data]);

  const update = (next: { view?: AgendaView; anchor?: string | null; day?: string | null }) => {
    const p = new URLSearchParams(params);
    if (next.view) p.set('view', next.view);
    if (next.anchor === null) p.delete('anchor');
    else if (next.anchor) p.set('anchor', next.anchor);
    if (next.day === null) p.delete('day');
    else if (next.day) p.set('day', next.day);
    setParams(p);
  };

  // La base des flèches : l'ancre de l'URL, sinon la fenêtre que le serveur a rendue pour aujourd'hui.
  // Changer de période ou de vue oublie le jour touché.
  const base = anchor ?? (data && !isPlaceholderData ? data.from : undefined);
  const onPrev = () => { if (base) update({ anchor: view === 'week' ? addDays(base, -7) : addMonthsFirstDay(base, -1), day: null }); };
  const onNext = () => { if (base) update({ anchor: view === 'week' ? addDays(base, 7) : addMonthsFirstDay(base, 1), day: null }); };
  const onToday = () => update({ anchor: null, day: null });
  const onViewChange = (v: AgendaView) => update({ view: v, day: null });

  const current = data && !isPlaceholderData ? data : undefined;
  const showToday = !!current && !inWindow(current.today, current);
  const title = data ? formatPeriodTitle(view, data.from, data.to) : '';

  if (!currentDashboard) {
    return <div className="text-white/40">Aucun dashboard sélectionné.</div>;
  }

  const openItem = findEcheance(data, openEcheanceId);
  const openRecurringItem = findRecurring(data, openRecurringId);

  // Le jour courant hors fenêtre arrive en tête de `days` avec les retards portés : il se montre au-dessus,
  // jamais dans la grille où il n'a pas de case.
  const todayOutside = !!data && !inWindow(data.today, data);
  const carriedToday = data && todayOutside ? data.days.find((d) => d.isToday) : undefined;
  const windowDays = data ? (todayOutside ? data.days.filter((d) => !d.isToday) : data.days) : [];

  const renderPeriod = (d: AgendaResult) => {
    if (d.view === 'month') {
      const selected = selectedDayOf(d, windowDays, dayParam);
      const selectedDay: AgendaDay = windowDays.find((day) => day.date === selected) ?? { date: selected, isToday: false, items: [], routine: [] };
      return (
        <>
          <AgendaMonthGrid from={d.from} to={d.to} days={windowDays} selected={selected} onSelect={(iso) => update({ day: iso })} />
          <div className="mt-3 -mx-2">
            <AgendaDayBlock day={selectedDay} ref={landingRef} onOpenEcheance={setOpenEcheanceId} onOpenRecurring={openRecurring} />
          </div>
        </>
      );
    }
    return <AgendaWeekGrid days={windowDays} from={d.from} to={d.to} todayRef={landingRef} onOpenEcheance={setOpenEcheanceId} onOpenRecurring={openRecurring} />;
  };

  return (
    <div className="animate-[fadeIn_0.15s_ease-out]">
      <AgendaHeader
        view={view}
        onViewChange={onViewChange}
        onPrev={onPrev}
        onNext={onNext}
        onToday={onToday}
        showToday={showToday}
        navDisabled={!base}
        title={title}
        calendar={data?.calendar}
      />

      <div className={`mt-4 transition-opacity ${isPlaceholderData ? 'opacity-60' : ''}`}>
        {isPending ? (
          <AgendaSkeleton />
        ) : isError || !data ? (
          <ErrorLine onRetry={() => { refetch(); }} />
        ) : (
          <>
            {carriedToday && (
              <div className="-mx-2 mb-3">
                <AgendaDayBlock day={carriedToday} withMonth onOpenEcheance={setOpenEcheanceId} onOpenRecurring={openRecurring} />
              </div>
            )}
            {renderPeriod(data)}
            <AgendaUpcomingList upcoming={data.upcoming} onOpenEcheance={setOpenEcheanceId} onOpenRecurring={openRecurring} />
          </>
        )}
      </div>

      {openEcheanceId != null && (
        <EcheanceSheet
          echeanceId={openEcheanceId}
          item={openItem}
          dashboardId={currentDashboard.id}
          onClose={() => setOpenEcheanceId(null)}
        />
      )}

      {/* La fiche vit tant que la ligne est dans les données : une occurrence ne quitte jamais l'écran par un geste, elle change de statut. */}
      {openRecurringItem && (
        <RecurringSheet item={openRecurringItem} dashboardId={currentDashboard.id} onClose={() => setOpenRecurringId(null)} />
      )}
    </div>
  );
};

export default Agenda;
