import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { echeancesApi } from '../../api/echeances';
import { useToast } from '../../hooks/useToast';

interface Props {
  echeanceId: number;
  dashboardId: number;
  onEdit: () => void;
  /** Appelé une fois la ligne supprimée : la feuille n'a plus rien à montrer. */
  onDeleted: () => void;
}

/**
 * Le pied de la feuille Échéance : « Modifier » en lien discret, « Supprimer » en deux gestes (le bouton,
 * puis la confirmation en ligne). Les documents rattachés restent, détachés par le serveur.
 */
export const EcheanceEditDelete = ({ echeanceId, dashboardId, onEdit, onDeleted }: Props) => {
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const [deleteAsked, setDeleteAsked] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const remove = useMutation({
    mutationFn: () => echeancesApi.remove(echeanceId),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['agenda', dashboardId] }),
        queryClient.invalidateQueries({ queryKey: ['echeances', dashboardId] }),
        queryClient.invalidateQueries({ queryKey: ['documents', dashboardId] }),
      ]);
      queryClient.removeQueries({ queryKey: ['echeance', echeanceId] });
      showToast('Échéance supprimée', 'success');
      onDeleted();
    },
    onError: () => setError('Suppression impossible, réessaie.'),
  });

  return (
    <div className="mt-5 pt-3 border-t border-white/10">
      {error && <p className="text-xs text-amber-300/90 mb-2">{error}</p>}
      <div className="flex items-center justify-between gap-3 text-sm">
        <button
          type="button"
          onClick={onEdit}
          disabled={remove.isPending}
          className="min-h-11 px-1 text-white/50 hover:text-white underline underline-offset-2 transition-colors disabled:opacity-50"
        >
          Modifier
        </button>
        {deleteAsked ? (
          <div className="flex items-center gap-3 text-white/70 min-h-11">
            <span>Supprimer ?</span>
            <button
              type="button"
              onClick={() => remove.mutate()}
              disabled={remove.isPending}
              className="min-h-11 px-3 rounded-lg bg-white/10 text-white hover:bg-white/15 disabled:opacity-50 transition-colors"
            >
              {remove.isPending ? '…' : 'Oui'}
            </button>
            <button
              type="button"
              onClick={() => setDeleteAsked(false)}
              disabled={remove.isPending}
              className="min-h-11 px-3 rounded-lg text-white/60 hover:text-white transition-colors"
            >
              Non
            </button>
          </div>
        ) : (
          <button
            type="button"
            onClick={() => setDeleteAsked(true)}
            className="min-h-11 px-1 text-white/40 hover:text-white/70 transition-colors"
          >
            Supprimer
          </button>
        )}
      </div>
    </div>
  );
};
