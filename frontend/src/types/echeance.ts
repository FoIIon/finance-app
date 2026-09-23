// Complément de types/agenda.ts (Echeance, UpdateEcheance y vivent déjà, rien n'y est renommé).

/** CreateEcheanceDto. dueDate en yyyy-MM-dd, amount null quand le montant n'est pas connu. */
export interface CreateEcheance {
  dashboardId: number;
  label: string;
  dueDate: string;
  amount: number | null;
  notes: string | null;
  /** Lot 3, facultatifs : saisie brute acceptée, le serveur normalise et refuse un contrôle 97 faux. */
  counterpartyIban: string | null;
  structuredCommunication: string | null;
  /** Nom du bénéficiaire pour le QR code de virement, facultatif, 70 caractères au plus (le serveur trime et refuse au-delà). */
  counterpartyName: string | null;
}
