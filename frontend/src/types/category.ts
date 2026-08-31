export interface Category {
  id: number;
  name: string;
  icon: string;
  color: string;
  isDefault: boolean;
  /** Catégorie de transfert : l'argent change de compte sans être consommé (épargne, titres). */
  isTransfer: boolean;
  /** Sortie du bilan mensuel : balayage du compte joint, virements entre comptes suivis. */
  excludeFromMonthlyReport: boolean;
}

export interface CreateCategory {
  name: string;
  icon: string;
  color: string;
}

export interface UpdateCategory {
  name: string;
  icon: string;
  color: string;
}
