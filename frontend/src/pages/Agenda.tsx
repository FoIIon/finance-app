import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useDashboards } from '../hooks/useDashboards';
import { useAgendaQuery } from '../hooks/queries';
import type { AgendaDay, AgendaEmptyRange, AgendaItem, AgendaResult, AgendaView } from '../types/agenda';
import { AgendaHeader } from '../components/agenda/AgendaHeader';
import { AgendaDayBlock } from '../components/agenda/AgendaDayBlock';
import { AgendaUpcomingList } from '../components/agenda/AgendaUpcomingList';
import { AgendaSkeleton } from '../components/agenda/AgendaSkeleton';
import { EcheanceSheet } from '../components/agenda/EcheanceSheet';
import { addDays, addMonthsFirstDay, formatEmptyRange, formatPeriodTitle } from '../components/agenda/agendaFormat';

const ISO_DAY = /^\d{4}-\d{2}-\d{2}$/;

type Entry = { kind: 'day'; day: AgendaDay } | { kind: 'empty'; range: AgendaEmptyRange };

/**
 * Remet les plages vides à leur place entre les jours, dans l'ordre des dates. L'ordre des jours est
 * celui du serveur : un jour courant porté en tête hors fenêtre y reste, on ne le reclasse pas.
 */
const interleave = (data: AgendaResult): Entry[] => {
  const out: Entry[] = [];
  let r = 0;
  for (const day of data.days) {
    const inWindow = day.date >= data.from && day.date <= data.to;
    while (inWindow && r < data.emptyRanges.length && data.emptyRanges[r].from < day.date) {
      out.push({ kind: 'empty', range: data.emptyRanges[r++] });
    }
    out.push({ kind: 'day', day });
  }
  while (r < data.emptyRanges.length) out.push({ kind: 'empty', range: data.emptyRanges[r++] });
  return out;
};

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
 * semaine ou au mois. `view` et `anchor` vivent dans l'URL, le retour arrière fonctionne et un lien se
 * partage. Aucune règle ici : statut, tri, retards portés, jours vides et routine arrivent du serveur.
 */
const Agenda = () => {
  const { currentDashboard } = useDashboards();
  const [params, setParams] = useSearchParams();
  const view: AgendaView = params.get('view') === 'month' ? 'month' : 'week';
  const anchorParam = params.get('anchor');
  const anchor = anchorParam && ISO_DAY.test(anchorParam) ? anchorParam : undefined;

  const dashboardId = currentDashboard?.id;
  const { data, isPending, isError, isPlaceholderData, refetch } = useAgendaQuery(dashboardId, view, anchor);

  const [openEcheanceId, setOpenEcheanceId] = useState<number | null>(null);

  // L'écran s'ouvre sur aujourd'hui, une seule fois, et seulement si le bloc n'est pas déjà visible.
  const todayRef = useRef<HTMLElement>(null);
  const scrolledOnce = useRef(false);
  useEffect(() => {
    if (!data || scrolledOnce.current) return;
    scrolledOnce.current = true;
    const el = todayRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    if (rect.bottom > window.innerHeight) el.scrollIntoView({ block: 'start' });
  }, [data]);

  const update = (next: { view?: AgendaView; anchor?: string | null }) => {
    const p = new URLSearchParams(params);
    if (next.view) p.set('view', next.view);
    if (next.anchor === null) p.delete('anchor');
    else if (next.anchor) p.set('anchor', next.anchor);
    setParams(p);
  };

  // La base des flèches : l'ancre de l'URL, sinon la fenêtre que le serveur a rendue pour aujourd'hui.
  const base = anchor ?? (data && !isPlaceholderData ? data.from : undefined);
  const onPrev = () => { if (base) update({ anchor: view === 'week' ? addDays(base, -7) : addMonthsFirstDay(base, -1) }); };
  const onNext = () => { if (base) update({ anchor: view === 'week' ? addDays(base, 7) : addMonthsFirstDay(base, 1) }); };
  const onToday = () => update({ anchor: null });

  const current = data && !isPlaceholderData ? data : undefined;
  const showToday = !!current && (current.today < current.from || current.today > current.to);
  const title = data ? formatPeriodTitle(view, data.from, data.to) : '';

  if (!currentDashboard) {
    return <div className="text-white/40">Aucun dashboard sélectionné.</div>;
  }

  const openItem = findEcheance(data, openEcheanceId);

  return (
    <div className="animate-[fadeIn_0.15s_ease-out]">
      <AgendaHeader
        view={view}
        onViewChange={(v) => update({ view: v })}
        onPrev={onPrev}
        onNext={onNext}
        onToday={onToday}
        showToday={showToday}
        navDisabled={!base}
        title={title}
        calendar={data?.calendar}
      />

      <div className={`mt-4 space-y-3 transition-opacity ${isPlaceholderData ? 'opacity-60' : ''}`}>
        {isPending ? (
          <AgendaSkeleton />
        ) : isError || !data ? (
          <ErrorLine onRetry={() => { refetch(); }} />
        ) : (
          <>
            {interleave(data).map((entry) =>
              entry.kind === 'day' ? (
                <AgendaDayBlock
                  key={`day:${entry.day.date}`}
                  day={entry.day}
                  ref={entry.day.isToday ? todayRef : undefined}
                  onOpenEcheance={setOpenEcheanceId}
                />
              ) : (
                <p key={`empty:${entry.range.from}`} className="text-sm text-white/40 px-4 py-1">
                  {formatEmptyRange(entry.range.from, entry.range.to)}
                </p>
              ),
            )}
            <AgendaUpcomingList upcoming={data.upcoming} onOpenEcheance={setOpenEcheanceId} />
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
    </div>
  );
};

export default Agenda;
