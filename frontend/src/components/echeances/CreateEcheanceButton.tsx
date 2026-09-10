import { useState } from 'react';
import { useDashboards } from '../../hooks/useDashboards';
import { EcheanceFormSheet } from './EcheanceFormSheet';

/**
 * « + Échéance », à droite du bandeau de l'Agenda. Le bouton porte lui-même sa feuille de création : le
 * bandeau n'a rien à savoir du dashboard courant ni de l'état de la feuille.
 */
export const CreateEcheanceButton = () => {
  const { currentDashboard } = useDashboards();
  const [open, setOpen] = useState(false);
  if (!currentDashboard) return null;

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="min-h-11 px-3 rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-300 text-sm font-medium hover:bg-amber-500/20 transition-colors whitespace-nowrap"
      >
        + Échéance
      </button>
      {open && <EcheanceFormSheet dashboardId={currentDashboard.id} onClose={() => setOpen(false)} />}
    </>
  );
};
