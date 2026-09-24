import { useMutation, useQueryClient } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { documentsApi } from '../../api/documents';
import { useMailSourceQuery } from '../../hooks/queries';
import { useToast } from '../../hooks/useToast';
import type { MailSource } from '../../types/documents';
import { formatRelative } from '../agenda/agendaFormat';

interface Props {
  dashboardId: number;
}

/** « 4 septembre » à partir d'un instant ISO UTC, dans le fuseau du navigateur. */
const formatInstantDayMonth = (iso: string) =>
  new Intl.DateTimeFormat('fr-FR', { day: 'numeric', month: 'long' }).format(new Date(iso));

/** En échec, la ligne dit quand on a essayé : sans ça, rien ne bouge à l'écran après « Relever maintenant ». */
const withAttempt = (text: string, s: MailSource) =>
  s.lastAttemptAt ? `${text} · dernière tentative ${formatRelative(s.lastAttemptAt)}` : text;

/** Une ligne par statut. Le serveur ne rend que des dates et des types d'exception, jamais un objet de mail. */
const statusLine = (s: MailSource): { text: string; warn: boolean } => {
  switch (s.lastSyncStatus) {
    case 'Ok': {
      const seen = s.lastSyncAt ? `Relevée ${formatRelative(s.lastSyncAt)}` : 'Relevée';
      const deposit = s.lastDepositAt ? `dernier dépôt le ${formatInstantDayMonth(s.lastDepositAt)}` : 'aucun dépôt encore';
      return { text: `${seen} · ${deposit}`, warn: false };
    }
    case 'AuthError':
      return { text: withAttempt("Mot de passe d'application refusé, à régénérer", s), warn: true };
    case 'ConnectionError':
      return { text: withAttempt('Boîte injoignable', s), warn: true };
    case 'Error':
      return { text: withAttempt('Relevé en échec', s), warn: true };
    default:
      return { text: 'Jamais relevée', warn: false };
  }
};

/**
 * La carte « Boîte factures » de la page Documents : rendue seulement quand le serveur connaît une source
 * (204 → rien du tout). Adresse relevée, ligne d'état, raison courte quand ça ne va pas, nombre de dépôts, et
 * « Relever maintenant », qui ouvre la boîte tout de suite sous le sémaphore du service de fond.
 */
export const MailSourceCard = ({ dashboardId }: Props) => {
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const { data } = useMailSourceQuery(dashboardId);

  const refresh = useMutation({
    mutationFn: () => documentsApi.refreshMailSource(dashboardId),
    onSuccess: async (res) => {
      const before = data?.depositedCount ?? 0;
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['mail-source', dashboardId] }),
        queryClient.invalidateQueries({ queryKey: ['documents', dashboardId] }),
      ]);
      // Le relevé répond 200 même quand la boîte n'a pas pu être ouverte : l'issue est dans le statut.
      if (res.data && res.data.lastSyncStatus !== 'Ok') {
        showToast(statusLine(res.data).text, 'error');
        return;
      }
      // Boîte ouverte mais un message laissé en échec (il sera retenté) : le statut est Ok, lastError le dit.
      if (res.data?.lastError) {
        showToast('Boîte relevée, un message reste en échec', 'warning');
        return;
      }
      const created = res.data ? res.data.depositedCount - before : 0;
      if (created > 0) showToast(created === 1 ? '1 nouveau document' : `${created} nouveaux documents`, 'success');
      else showToast('Boîte relevée', 'success');
    },
    onError: (err) => {
      const status = isAxiosError(err) ? err.response?.status : undefined;
      // 409 et 429 ne sont pas des échecs : un relevé tourne déjà, ou on a assez cliqué. En rouge, ils
      // invitaient à recliquer, ce qui consomme le seau du 429.
      if (status === 409) showToast('Relevé déjà en cours, il se termine seul', 'warning');
      else if (status === 429) showToast('Trop de relevés, réessaie dans une minute', 'warning');
      else showToast('Relevé impossible, réessaie.');
    },
  });

  if (!data) return null;

  const line = statusLine(data);
  const count = data.depositedCount;

  return (
    <section aria-label="Boîte factures" className="rounded-xl border border-white/10 bg-white/5 p-3 space-y-2">
      <div className="flex items-start justify-between gap-3 flex-wrap">
        <div className="min-w-0 flex-1">
          <h3 className="text-sm font-semibold text-white">Boîte factures</h3>
          <p className="text-xs text-white/50 break-words">{data.address}</p>
        </div>
        <button
          type="button"
          onClick={() => refresh.mutate()}
          disabled={refresh.isPending}
          className="min-h-11 px-3 rounded-lg bg-white/5 border border-white/10 text-white/70 text-sm hover:text-white hover:bg-white/10 transition-colors disabled:opacity-50 whitespace-nowrap"
        >
          {refresh.isPending ? 'Relevé…' : 'Relever maintenant'}
        </button>
      </div>
      <p className={`text-sm break-words min-h-11 flex items-center ${line.warn ? 'text-amber-300/90' : 'text-white/70'}`}>{line.text}</p>
      {data.lastError && (
        <p className="text-xs text-white/40 break-words">{data.lastError}</p>
      )}
      <p className="text-xs text-white/40">
        {count === 0 ? 'Aucun document déposé' : count === 1 ? '1 document déposé' : `${count} documents déposés`}
      </p>
    </section>
  );
};
