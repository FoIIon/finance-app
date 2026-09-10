import { useCallback, useState } from 'react';
import { documentsApi } from '../../api/documents';
import { serverMessageOf } from './documentFormat';

export interface ViewerTarget {
  url: string;
  contentType: string;
  name: string;
}

/**
 * Le viewer (modale plein écran dans l'app) est le mode par défaut dès qu'on est en PWA installée ou sur
 * un écran tactile : Safari iOS en « standalone » ouvre les fenêtres dans une vue séparée qui ne résout
 * pas l'object URL de l'ouvreur, et l'onglet d'attente resterait figé. L'onglet reste le mode du bureau
 * (pointeur fin, hors standalone). Décidé au moment du geste, pas au montage.
 */
const prefersViewer = () =>
  window.matchMedia('(display-mode: standalone)').matches || window.matchMedia('(pointer: coarse)').matches;

/** L'onglet ouvert attend le contenu : une ligne fixe sur le fond de l'app, le nom posé en titre (jamais écrit en HTML). */
const writeWaiting = (win: Window, name: string) => {
  try {
    win.document.write('<!doctype html><title>Document</title><body style="margin:0;background:#0a0a1a;color:#ffffff99;font:14px system-ui;display:flex;align-items:center;justify-content:center;height:100vh">Chargement du document…</body>');
    win.document.close();
    win.document.title = name;
  } catch {
    // Fenêtre déjà naviguée ou inaccessible : on la laissera charger à l'aveugle.
  }
};

/**
 * Ouvre un document avec le jeton : le contenu arrive par apiClient en blob et devient un object URL.
 * Tactile ou PWA : le viewer, directement. Bureau : un onglet ouvert **de manière synchrone dans le geste**
 * (sinon le navigateur le bloque) y navigue une fois la réponse là ; s'il a été bloqué, le viewer prend le
 * relais ; si l'appel échoue, l'onglet d'attente se ferme et l'erreur s'écrit en une ligne, sans modale
 * surprise. Jamais d'URL portant le jeton, jamais de HTML injecté.
 */
export const useOpenDocument = () => {
  const [pendingId, setPendingId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [viewer, setViewer] = useState<ViewerTarget | null>(null);

  const open = useCallback(async (id: number, name: string) => {
    const viewerMode = prefersViewer();
    // Mode onglet : première instruction, avant tout await, la fenêtre doit naître pendant le clic.
    const win = viewerMode ? null : window.open('', '_blank');
    if (win) writeWaiting(win, name);
    setPendingId(id);
    setError(null);
    try {
      const res = await documentsApi.content(id);
      const blob = res.data;
      if (!win) {
        setViewer({ url: URL.createObjectURL(blob), contentType: blob.type, name });
        return;
      }
      // L'onglet d'attente a été fermé avant la réponse : rien à montrer, et pas de modale à sa place.
      if (win.closed) return;
      const url = URL.createObjectURL(blob);
      win.location.href = url;
      // L'onglet a le temps de lire le blob ; ensuite l'URL ne sert plus.
      window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
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
