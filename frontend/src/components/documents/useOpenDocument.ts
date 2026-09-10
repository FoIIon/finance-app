import { useCallback, useState } from 'react';
import { documentsApi } from '../../api/documents';
import { serverMessageOf } from './documentFormat';

export interface ViewerTarget {
  url: string;
  contentType: string;
  name: string;
}

/** L'onglet ouvert attend le contenu : une ligne dessus, sur le fond de l'app, plutôt qu'un blanc. */
const writeWaiting = (win: Window) => {
  try {
    win.document.write('<!doctype html><title>Document</title><body style="margin:0;background:#0a0a1a;color:#ffffff99;font:14px system-ui;display:flex;align-items:center;justify-content:center;height:100vh">Chargement du document…</body>');
    win.document.close();
  } catch {
    // Fenêtre déjà naviguée ou inaccessible : on la laissera charger à l'aveugle.
  }
};

/**
 * Ouvre un document avec le jeton : le contenu arrive par apiClient en blob, devient un object URL, et
 * l'onglet ouvert **de manière synchrone dans le geste** (sinon Safari iOS le bloque) y navigue une fois
 * la réponse là. Si le navigateur n'a pas rendu de fenêtre, le même object URL s'affiche dans une modale
 * plein écran (`viewer`). Jamais d'URL portant le jeton, jamais de HTML injecté.
 */
export const useOpenDocument = () => {
  const [pendingId, setPendingId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [viewer, setViewer] = useState<ViewerTarget | null>(null);

  const open = useCallback(async (id: number, name: string) => {
    // Première instruction, avant tout await : la fenêtre doit naître pendant le clic.
    const win = window.open('', '_blank');
    if (win) writeWaiting(win);
    setPendingId(id);
    setError(null);
    try {
      const res = await documentsApi.content(id);
      const blob = res.data;
      const url = URL.createObjectURL(blob);
      if (win && !win.closed) {
        win.location.href = url;
        // L'onglet a le temps de lire le blob ; ensuite l'URL ne sert plus.
        window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
      } else {
        setViewer({ url, contentType: blob.type, name });
      }
    } catch (err) {
      if (win && !win.closed) win.close();
      setError(serverMessageOf(err, "Impossible d'ouvrir le document, réessaie."));
    } finally {
      setPendingId(null);
    }
  }, []);

  const closeViewer = useCallback(() => {
    setViewer((v) => {
      if (v) URL.revokeObjectURL(v.url);
      return null;
    });
  }, []);

  return { open, pendingId, error, viewer, closeViewer };
};
