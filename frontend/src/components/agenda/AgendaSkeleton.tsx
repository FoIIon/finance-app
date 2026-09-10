/** Squelette gris de sept jours compacts pendant le premier chargement, à la hauteur des vrais. Pas de spinner plein écran. */
export const AgendaSkeleton = () => (
  <div className="-mx-2 animate-pulse" aria-busy="true" aria-label="Chargement de l'agenda">
    {[0, 1, 2, 3, 4, 5, 6].map((i) => (
      <div key={i} className="border-b border-white/5 border-l-2 border-l-transparent px-1.5">
        <div className="h-7 flex items-center">
          <div className="h-3 w-28 rounded bg-white/10" />
        </div>
        {i % 3 === 0 && (
          <div className="min-h-10 flex items-center">
            <div className="ml-15 h-3 w-2/3 rounded bg-white/5" />
          </div>
        )}
      </div>
    ))}
  </div>
);
