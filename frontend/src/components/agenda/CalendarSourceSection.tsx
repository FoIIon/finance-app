import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { calendarApi } from '../../api/calendar';
import { useCalendarSourceQuery } from '../../hooks/queries';
import type { CalendarStatus } from '../../types/agenda';
import { formatInstantDay, formatRelative } from './agendaFormat';

interface Props {
  dashboardId: number;
}

const statusOf = (err: unknown) => (isAxiosError(err) ? err.response?.status : undefined);

/** Le texte brut du serveur sur un 400 (IcsUrlPolicy), sinon une phrase neutre. Jamais l'adresse. */
const refusalOf = (err: unknown) => {
  if (statusOf(err) === 400) {
    const data = isAxiosError(err) ? err.response?.data : undefined;
    if (typeof data === 'string' && data.trim()) return data;
    return 'Adresse refusée.';
  }
  return 'Connexion impossible, réessaie.';
};

/** Le statut de synchronisation en texte quand il n'est pas Ok. */
const syncStatusText = (c: CalendarStatus) => {
  const when = c.lastAttemptAt ? ` le ${formatInstantDay(c.lastAttemptAt)}` : '';
  switch (c.lastSyncStatus) {
    case 'HttpError':
      return `Erreur de lecture${when}`;
    case 'Invalid':
      return `Contenu illisible${when}`;
    case 'KeyLost':
      return 'Clé de chiffrement perdue : l\'adresse doit être ressaisie';
    default:
      return null;
  }
};

/**
 * Section « Calendrier familial » des paramètres. L'adresse ICS est un secret : elle part dans le corps
 * du PUT, le champ se vide dès l'envoi, elle n'est jamais réaffichée ni en clair ni masquée. Le seul
 * repère est le nom du calendrier renvoyé par le serveur.
 */
export const CalendarSourceSection = ({ dashboardId }: Props) => {
  const queryClient = useQueryClient();
  const { data, isLoading, isError, refetch } = useCalendarSourceQuery(dashboardId);
  const [url, setUrl] = useState('');
  const [formError, setFormError] = useState<string | null>(null);
  const [refreshMessage, setRefreshMessage] = useState<string | null>(null);
  const [disconnectAsked, setDisconnectAsked] = useState(false);

  const invalidate = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['calendar-source', dashboardId] }),
      queryClient.invalidateQueries({ queryKey: ['agenda', dashboardId] }),
    ]);
  };

  // Synchronise dans la requête, jusqu'à 30 s : le bouton porte l'attente.
  const connect = useMutation({
    mutationFn: (value: string) => calendarApi.putSource(dashboardId, value),
    onSuccess: () => { setFormError(null); return invalidate(); },
    onError: (err) => setFormError(refusalOf(err)),
  });

  const refresh = useMutation({
    mutationFn: () => calendarApi.refresh(dashboardId),
    onSuccess: () => { setRefreshMessage(null); return invalidate(); },
    onError: (err) =>
      setRefreshMessage(
        statusOf(err) === 429 ? 'Trop de rafraîchissements, réessaie dans une minute' : 'Rafraîchissement impossible, réessaie.',
      ),
  });

  const disconnect = useMutation({
    mutationFn: () => calendarApi.deleteSource(dashboardId),
    onSuccess: () => { setDisconnectAsked(false); setRefreshMessage(null); return invalidate(); },
    onError: () => setRefreshMessage('Déconnexion impossible, réessaie.'),
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const value = url.trim();
    // Vidé dès l'envoi, succès ou refus : le secret ne reste pas dans le champ.
    setUrl('');
    if (!value) return;
    setFormError(null);
    connect.mutate(value);
  };

  const form = (
    <form onSubmit={handleSubmit} className="space-y-2">
      <label htmlFor="calendar-ics-url" className="block text-white/40 text-sm">
        Adresse ICS privée du calendrier Google
      </label>
      <input
        id="calendar-ics-url"
        type="url"
        inputMode="url"
        autoComplete="off"
        spellCheck={false}
        value={url}
        onChange={(e) => setUrl(e.target.value)}
        disabled={connect.isPending}
        placeholder="https://calendar.google.com/calendar/ical/…/private-…/basic.ics"
        className="w-full px-3 py-2.5 rounded-lg bg-white/5 border border-white/10 text-white text-sm placeholder-white/30 focus:outline-none focus:border-amber-500/50 disabled:opacity-50"
      />
      <p className="text-xs text-white/40">Google Agenda › Paramètres du calendrier › Adresse secrète au format iCal</p>
      {formError && <p className="text-xs text-amber-300/90">{formError}</p>}
      <button
        type="submit"
        disabled={connect.isPending}
        className="w-full sm:w-auto min-h-11 px-4 rounded-lg bg-amber-500/20 text-amber-400 text-sm font-medium hover:bg-amber-500/30 transition-colors disabled:opacity-50 inline-flex items-center justify-center gap-2"
      >
        {connect.isPending && (
          <span aria-hidden="true" className="inline-block w-3.5 h-3.5 rounded-full border-2 border-amber-400/40 border-t-amber-400 animate-spin" />
        )}
        {connect.isPending ? 'Lecture du calendrier…' : 'Connecter'}
      </button>
    </form>
  );

  let body: React.ReactNode;
  if (isLoading) {
    body = <p className="text-white/40 text-sm">Chargement…</p>;
  } else if (isError || !data) {
    body = (
      <p className="text-white/50 text-sm">
        Impossible de lire l'état du calendrier ·{' '}
        <button type="button" onClick={() => refetch()} className="underline underline-offset-2 hover:text-white">Réessayer</button>
      </p>
    );
  } else if (!data.connected) {
    body = form;
  } else {
    const pending = data.lastSyncStatus === 'Pending' || !data.lastSyncAt;
    const statusText = syncStatusText(data);
    body = (
      <div className="space-y-3">
        <p className="text-white/70 break-words">
          Calendrier connecté{data.calendarName ? ` : ${data.calendarName}` : ''}
        </p>
        <p className="text-white/40 text-sm">
          Dernière lecture : {pending ? 'en attente' : formatRelative(data.lastSyncAt!)}
        </p>
        {statusText && <p className="text-amber-300/90 text-sm">{statusText}</p>}
        {data.lastError && data.lastSyncStatus !== 'Ok' && (
          <p className="text-white/40 text-xs break-words">{data.lastError}</p>
        )}
        {refreshMessage && <p className="text-amber-300/90 text-xs">{refreshMessage}</p>}

        <div className="flex flex-col sm:flex-row gap-2 pt-1">
          <button
            type="button"
            onClick={() => refresh.mutate()}
            disabled={refresh.isPending || disconnect.isPending}
            className="w-full sm:w-auto min-h-11 px-4 rounded-lg bg-white/5 border border-white/10 text-white/70 text-sm hover:text-white hover:bg-white/10 transition-colors disabled:opacity-50"
          >
            {refresh.isPending ? 'Lecture…' : 'Rafraîchir'}
          </button>
          {disconnectAsked ? (
            <div className="flex items-center gap-3 text-sm text-white/70 min-h-11">
              <span>Déconnecter ?</span>
              <button
                type="button"
                onClick={() => disconnect.mutate()}
                disabled={disconnect.isPending}
                className="min-h-11 px-3 rounded-lg bg-white/10 text-white hover:bg-white/15 disabled:opacity-50 transition-colors"
              >
                {disconnect.isPending ? '…' : 'Oui'}
              </button>
              <button
                type="button"
                onClick={() => setDisconnectAsked(false)}
                className="min-h-11 px-3 rounded-lg text-white/60 hover:text-white transition-colors"
              >
                Non
              </button>
            </div>
          ) : (
            <button
              type="button"
              onClick={() => setDisconnectAsked(true)}
              disabled={refresh.isPending}
              className="w-full sm:w-auto min-h-11 px-4 rounded-lg text-white/40 text-sm hover:text-white/70 transition-colors disabled:opacity-50"
            >
              Déconnecter
            </button>
          )}
        </div>

        {data.lastSyncStatus === 'KeyLost' && (
          <div className="border-t border-white/10 pt-4 mt-2">
            <p className="text-white/40 text-sm mb-2">Ressaisir l'adresse :</p>
            {form}
          </div>
        )}
      </div>
    );
  }

  return (
    <section className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-4 sm:p-6">
      <h3 className="text-lg font-semibold text-white mb-4">Calendrier familial</h3>
      {body}
    </section>
  );
};
