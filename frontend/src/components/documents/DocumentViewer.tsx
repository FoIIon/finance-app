import { useEffect } from 'react';
import { createPortal } from 'react-dom';
import type { ViewerTarget } from './useOpenDocument';

interface Props {
  target: ViewerTarget;
  onClose: () => void;
}

/**
 * Le document plein écran, dans l'app, sur l'object URL : mode par défaut sur tactile et en PWA, repli sur
 * bureau quand l'onglet a été bloqué. Une image dans un <img>, le reste (PDF) dans un <iframe>. Gestes
 * secondaires : « Ouvrir dans un onglet », et « Enregistrer » qui porte le nom d'origine (`download`),
 * sinon le navigateur proposerait l'identifiant du blob. Le titre de la page prend le nom le temps de
 * l'affichage. Rien n'est injecté en HTML.
 */
export const DocumentViewer = ({ target, onClose }: Props) => {
  useEffect(() => {
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onEsc);
    return () => document.removeEventListener('keydown', onEsc);
  }, [onClose]);

  useEffect(() => {
    const previous = document.title;
    document.title = target.name;
    return () => { document.title = previous; };
  }, [target.name]);

  const isImage = target.contentType.startsWith('image/');
  const linkClass = 'min-h-11 inline-flex items-center px-2 text-sm text-white/60 hover:text-white transition-colors whitespace-nowrap';

  return createPortal(
    <div role="dialog" aria-modal="true" aria-label={target.name} className="fixed inset-0 z-[60] bg-[#0a0a1a] flex flex-col">
      <div className="flex items-center gap-2 px-3 py-1 border-b border-white/10 pt-[max(0.25rem,env(safe-area-inset-top))]">
        <p className="min-w-0 flex-1 text-sm text-white/80 truncate" title={target.name}>{target.name}</p>
        <a href={target.url} target="_blank" rel="noopener noreferrer" className={linkClass}>
          Ouvrir dans un onglet
        </a>
        <a href={target.url} download={target.name} className={linkClass}>
          Enregistrer
        </a>
        <button type="button" onClick={onClose} aria-label="Fermer" className="text-white/60 hover:text-white text-2xl leading-none px-2 min-h-11 min-w-11">
          ×
        </button>
      </div>
      <div className="flex-1 min-h-0 flex items-center justify-center bg-black/40">
        {isImage ? (
          <img src={target.url} alt={target.name} className="max-w-full max-h-full object-contain" />
        ) : (
          <iframe src={target.url} title={target.name} className="w-full h-full border-0 bg-white" />
        )}
      </div>
    </div>,
    document.body,
  );
};
