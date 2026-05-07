import { UncategorizedList } from '../../components/dashboard/UncategorizedList';
import { AnomaliesList } from '../../components/dashboard/AnomaliesList';

const DashboardTriage = () => {
  return (
    <div className="space-y-5">
      <div className="bg-gradient-to-br from-amber-500/5 to-orange-600/5 backdrop-blur-xl rounded-2xl border border-amber-500/20 p-5">
        <h3 className="text-base md:text-lg font-semibold text-amber-300">À faire ce lundi matin</h3>
        <p className="text-white/60 text-xs md:text-sm mt-1">
          Catégorise les transactions importées et repère les dépenses inhabituelles. 5 minutes pour garder le tableau de bord propre.
        </p>
      </div>

      <UncategorizedList />
      <AnomaliesList />
    </div>
  );
};

export default DashboardTriage;
