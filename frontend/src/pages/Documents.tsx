import { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { documentsApi } from '../api/documents';
import { useDashboards } from '../hooks/useDashboards';
import { useDocumentsQuery, useEcheancesQuery } from '../hooks/queries';
import { useToast } from '../hooks/useToast';
import type { Echeance } from '../types/agenda';
import { DOCUMENT_KINDS, type Document, type DocumentKind } from '../types/documents';
import { EcheanceSheet } from '../components/agenda/EcheanceSheet';
import { EcheanceFormSheet } from '../components/echeances/EcheanceFormSheet';
import { Sheet } from '../components/echeances/Sheet';
import { DocumentCard } from '../components/documents/DocumentCard';
import { DocumentUpload } from '../components/documents/DocumentUpload';
import { DocumentViewer } from '../components/documents/DocumentViewer';
import { DOCUMENT_KIND_LABELS, fileNameWithoutExtension } from '../components/documents/documentFormat';
import { useOpenDocument } from '../components/documents/useOpenDocument';

type YearFilter = number | 'all';

const isKind = (value: string | null): value is DocumentKind => !!value && (DOCUMENT_KINDS as readonly string[]).includes(value);

const Skeleton = () => (
  <ul className="grid gap-2 md:grid-cols-2 xl:grid-cols-3 animate-pulse" aria-busy="true" aria-label="Chargement des documents">
    {[0, 1, 2].map((i) => (
      <li key={i} className="rounded-xl border border-white/10 bg-white/5 p-3 h-24">
        <div className="h-3 w-2/3 rounded bg-white/10" />
        <div className="h-2.5 w-1/2 rounded bg-white/5 mt-2" />
      </li>
    ))}
  </ul>
);

/**
 * La route /documents : les papiers du ménage par année fiscale, en cartes. Pastilles d'année (celles des
 * données plus l'année en cours, « Toutes » en tête), un filtre discret par nature, « Déposer » en haut.
 * Les filtres vivent dans l'URL. Le serveur filtre et ordonne, la page n'invente aucun ordre.
 */
const Documents = () => {
  const { currentDashboard } = useDashboards();
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const [params, setParams] = useSearchParams();

  const currentYear = new Date().getFullYear();
  const yearParam = params.get('year');
  const year: YearFilter = yearParam === 'all' ? 'all' : yearParam && /^\d{4}$/.test(yearParam) ? Number(yearParam) : currentYear;
  const kindParam = params.get('kind');
  const kind = isKind(kindParam) ? kindParam : undefined;

  const dashboardId = currentDashboard?.id;
  const all = useDocumentsQuery(dashboardId);
  const list = useDocumentsQuery(dashboardId, { fiscalYear: year === 'all' ? undefined : year, kind });
  const echeances = useEcheancesQuery(dashboardId);

  const [uploadOpen, setUploadOpen] = useState(false);
  const [justUploaded, setJustUploaded] = useState<Document | null>(null);
  const [createFrom, setCreateFrom] = useState<Document | null>(null);
  const [openEcheanceId, setOpenEcheanceId] = useState<number | null>(null);
  const { open, pendingId, error: openError, viewer, closeViewer } = useOpenDocument();

  const years = useMemo(() => {
    const set = new Set<number>([currentYear]);
    for (const d of all.data ?? []) if (d.fiscalYear != null) set.add(d.fiscalYear);
    return [...set].sort((a, b) => b - a);
  }, [all.data, currentYear]);

  const labelOf = useMemo(() => {
    const map = new Map<number, string>();
    for (const e of echeances.data ?? []) map.set(e.id, e.label);
    return map;
  }, [echeances.data]);

  const update = (next: { year?: YearFilter; kind?: DocumentKind | null }) => {
    const p = new URLSearchParams(params);
    if (next.year !== undefined) {
      if (next.year === currentYear) p.delete('year');
      else p.set('year', String(next.year));
    }
    if (next.kind === null) p.delete('kind');
    else if (next.kind) p.set('kind', next.kind);
    setParams(p);
  };

  // Rattache le document déposé à l'échéance qui vient d'être créée à partir de lui (PUT des métadonnées).
  const attach = useMutation({
    mutationFn: ({ doc, echeance }: { doc: Document; echeance: Echeance }) =>
      documentsApi.update(doc.id, { kind: doc.kind, fiscalYear: doc.fiscalYear, echeanceId: echeance.id }),
    onSuccess: async (_res, { echeance }) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['documents', dashboardId] }),
        queryClient.invalidateQueries({ queryKey: ['echeance', echeance.id] }),
      ]);
      showToast(`Document rattaché à « ${echeance.label} »`, 'success');
      setJustUploaded(null);
    },
    onError: () => showToast("Échéance créée, mais le document n'a pas pu être rattaché.", 'error'),
  });

  const onUploaded = (doc: Document) => {
    setUploadOpen(false);
    setJustUploaded(doc);
    // La carte doit se voir : on suit l'année du document déposé, et on lève un filtre de nature qui la cacherait.
    const nextYear: YearFilter = doc.fiscalYear ?? 'all';
    update({ year: nextYear !== year ? nextYear : undefined, kind: kind && kind !== doc.kind ? null : undefined });
  };

  if (!currentDashboard) {
    return <div className="text-white/40">Aucun dashboard sélectionné.</div>;
  }

  const emptyText = (() => {
    const scope = year === 'all' ? '' : ` pour ${year}`;
    const nature = kind ? ` « ${DOCUMENT_KIND_LABELS[kind]} »` : '';
    return `Aucun document${nature}${scope}`;
  })();

  return (
    <div className="space-y-5 animate-[fadeIn_0.15s_ease-out]">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-2xl md:text-3xl font-bold text-white" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
          Documents
        </h2>
        <button
          type="button"
          onClick={() => setUploadOpen(true)}
          className="min-h-11 px-4 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white text-sm font-semibold hover:from-amber-600 hover:to-orange-700 transition-all whitespace-nowrap"
        >
          Déposer
        </button>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <div role="group" aria-label="Année fiscale" className="flex flex-wrap gap-1.5">
          <button
            type="button"
            aria-pressed={year === 'all'}
            onClick={() => update({ year: 'all' })}
            className={`min-h-9 px-3 rounded-full text-sm border transition-colors ${
              year === 'all' ? 'bg-amber-500/20 text-amber-300 border-amber-500/30' : 'text-white/60 border-white/10 hover:text-white hover:bg-white/5'
            }`}
          >
            Toutes
          </button>
          {years.map((y) => (
            <button
              key={y}
              type="button"
              aria-pressed={year === y}
              onClick={() => update({ year: y })}
              className={`min-h-9 px-3 rounded-full text-sm tabular-nums border transition-colors ${
                year === y ? 'bg-amber-500/20 text-amber-300 border-amber-500/30' : 'text-white/60 border-white/10 hover:text-white hover:bg-white/5'
              }`}
            >
              {y}
            </button>
          ))}
        </div>
        <select
          aria-label="Nature du document"
          value={kind ?? ''}
          onChange={(e) => update({ kind: isKind(e.target.value) ? e.target.value : null })}
          className="ml-auto min-h-9 px-2 rounded-lg bg-white/5 border border-white/10 text-sm text-white/60 focus:outline-none focus:border-amber-500/50 [color-scheme:dark]"
        >
          <option value="">Tous les types</option>
          {DOCUMENT_KINDS.map((k) => (
            <option key={k} value={k}>{DOCUMENT_KIND_LABELS[k]}</option>
          ))}
        </select>
      </div>

      {justUploaded && (
        <div className="flex items-start justify-between gap-3 rounded-xl border border-amber-500/20 bg-amber-500/5 px-3 py-2 text-sm">
          <p className="min-w-0 text-white/70 break-words">
            <span className="text-white">{justUploaded.originalFileName}</span> déposé ·{' '}
            <button
              type="button"
              onClick={() => setCreateFrom(justUploaded)}
              disabled={attach.isPending}
              className="text-amber-300/90 underline underline-offset-2 hover:text-amber-200 disabled:opacity-50 transition-colors text-left"
            >
              Créer une échéance à partir de ce document
            </button>
          </p>
          <button type="button" onClick={() => setJustUploaded(null)} aria-label="Ignorer" className="text-white/40 hover:text-white text-xl leading-none px-1 min-h-9">
            ×
          </button>
        </div>
      )}

      {openError && <p className="text-xs text-amber-300/90 break-words">{openError}</p>}

      {list.isPending ? (
        <Skeleton />
      ) : list.isError || !list.data ? (
        <p className="text-sm text-white/50 py-4">
          Impossible de charger ·{' '}
          <button type="button" onClick={() => { list.refetch(); }} className="underline underline-offset-2 hover:text-white transition-colors">
            Réessayer
          </button>
        </p>
      ) : list.data.length === 0 ? (
        <p className="text-sm text-white/50 py-4">
          {emptyText} ·{' '}
          <button type="button" onClick={() => setUploadOpen(true)} className="underline underline-offset-2 text-amber-300/90 hover:text-amber-200 transition-colors">
            Déposer le premier
          </button>
        </p>
      ) : (
        <ul className="grid gap-2 md:grid-cols-2 xl:grid-cols-3">
          {list.data.map((doc) => (
            <DocumentCard
              key={doc.id}
              doc={doc}
              dashboardId={currentDashboard.id}
              echeanceLabel={doc.echeanceId != null ? labelOf.get(doc.echeanceId) : undefined}
              onOpen={open}
              opening={pendingId === doc.id}
              onOpenEcheance={setOpenEcheanceId}
            />
          ))}
        </ul>
      )}

      {uploadOpen && (
        <Sheet titleId="document-upload-title" title="Déposer un document" onClose={() => setUploadOpen(false)}>
          <DocumentUpload
            dashboardId={currentDashboard.id}
            onUploaded={onUploaded}
            onOpenExisting={(id, name) => { setUploadOpen(false); open(id, name); }}
          />
        </Sheet>
      )}

      {createFrom && (
        <EcheanceFormSheet
          dashboardId={currentDashboard.id}
          defaults={{ label: fileNameWithoutExtension(createFrom.originalFileName) }}
          onClose={() => setCreateFrom(null)}
          onSaved={(echeance) => attach.mutate({ doc: createFrom, echeance })}
        />
      )}

      {openEcheanceId != null && (
        <EcheanceSheet echeanceId={openEcheanceId} item={undefined} dashboardId={currentDashboard.id} onClose={() => setOpenEcheanceId(null)} />
      )}

      {viewer && <DocumentViewer target={viewer} onClose={closeViewer} />}
    </div>
  );
};

export default Documents;
