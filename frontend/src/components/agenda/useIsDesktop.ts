import { useSyncExternalStore } from 'react';

/** Le `lg:` de Tailwind : le point de rupture où la semaine passe en sept colonnes. */
const QUERY = '(min-width: 1024px)';

const subscribe = (onChange: () => void) => {
  const mq = window.matchMedia(QUERY);
  mq.addEventListener('change', onChange);
  return () => mq.removeEventListener('change', onChange);
};

const getSnapshot = () => window.matchMedia(QUERY).matches;

/**
 * Vrai à partir de 1024 px, suit le redimensionnement. Sert à choisir le gabarit (ligne ou colonne) d'un
 * même jour sans rendre deux fois le DOM : un seul en-tête « Aujourd'hui » à l'écran, quel que soit l'écran.
 */
export const useIsDesktop = () => useSyncExternalStore(subscribe, getSnapshot, () => false);
