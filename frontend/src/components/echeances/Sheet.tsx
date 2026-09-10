import { useEffect, type ReactNode } from 'react';
import { createPortal } from 'react-dom';

interface Props {
  /** Identifiant du titre, pour aria-labelledby. */
  titleId: string;
  title: string;
  onClose: () => void;
  children: ReactNode;
}

/**
 * Feuille basse sur le patron d'EcheanceSheet (lot 2) : collée en bas sur téléphone, centrée sur bureau,
 * fermée par le fond, la croix ou Échap. Elle ne porte aucun état, le contenu décide de tout.
 */
export const Sheet = ({ titleId, title, onClose, children }: Props) => {
  useEffect(() => {
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onEsc);
    return () => document.removeEventListener('keydown', onEsc);
  }, [onClose]);

  return createPortal(
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-end md:items-center justify-center"
      onClick={onClose}
    >
      <div
        className="bg-[#1a1a3e] rounded-t-2xl md:rounded-2xl border border-white/10 p-6 pb-[max(1.5rem,env(safe-area-inset-bottom))] w-full md:max-w-md max-h-[85vh] overflow-y-auto"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-3 mb-4">
          <h3 id={titleId} className="text-xl font-bold text-white break-words">{title}</h3>
          <button type="button" onClick={onClose} aria-label="Fermer" className="text-white/40 hover:text-white text-2xl leading-none px-2 min-h-11">
            ×
          </button>
        </div>
        {children}
      </div>
    </div>,
    document.body,
  );
};
