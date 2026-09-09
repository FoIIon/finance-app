/** Squelette gris de trois jours pendant le premier chargement. Pas de spinner plein écran. */
export const AgendaSkeleton = () => (
  <div className="space-y-3" aria-busy="true" aria-label="Chargement de l'agenda">
    {[0, 1, 2].map((i) => (
      <div key={i} className="rounded-2xl border border-white/10 bg-white/5 p-4 animate-pulse">
        <div className="h-4 w-40 rounded bg-white/10" />
        <div className="mt-3 space-y-2">
          <div className="h-3 w-3/4 rounded bg-white/5" />
          <div className="h-3 w-1/2 rounded bg-white/5" />
        </div>
      </div>
    ))}
  </div>
);
