// Complément de types/agenda.ts (Echeance, UpdateEcheance y vivent déjà, rien n'y est renommé).

/** CreateEcheanceDto. dueDate en yyyy-MM-dd, amount null quand le montant n'est pas connu. */
export interface CreateEcheance {
  dashboardId: number;
  label: string;
  dueDate: string;
  amount: number | null;
  notes: string | null;
}
