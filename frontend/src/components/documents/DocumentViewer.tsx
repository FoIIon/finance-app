import { useEffect } from 'react';
import { createPortal } from 'react-dom';
import type { ViewerTarget } from './useOpenDocument';

interface Props {
  target: ViewerTarget;
  onClose: () => void;
}

/**
 * Repli quand le navigateur n'a pas ouvert d'onglet : le document plein écran, dans l'app, sur l'object URL.
 * Une image dans un <img>, le reste (PDF) dans un <iframe>. Rien n'est injecté en HTML.
 */
export const DocumentViewer = ({ target, onClose }: Props) => {
  useEffect(() => {
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onEsc);
    return () => document.removeEventListener('keydown', onEsc);
  }, [onClose]);

  const isImage = target.contentType.startsWith('image/');

  return createPortal(
    <div role="dialog" aria-modal="true" aria-label={target.name} className="fixed inset-0 z-[60] bg-[#0a0a1a] flex flex-col">
      <div className="flex items-center justify-between gap-3 px-4 py-2 border-b border-white/10 pt-[max(0.5rem,env(safe-area-inset-top))]">
        <p className="min-w-0 text-sm text-white/80 truncate">{target.name}</p>
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
