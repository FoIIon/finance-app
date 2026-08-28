export const BankConnectionStatus = {
  Linked: 0,
  Expired: 1,
  Error: 2,
  PendingTwoFactor: 3,
  PendingAuthorization: 4,
} as const;

export type BankConnectionStatus = (typeof BankConnectionStatus)[keyof typeof BankConnectionStatus];

export interface Institution {
  id: string;
  name: string;
  logo: string;
  countries: string[];
}

export interface BankAccount {
  id: number;
  externalAccountId: string;
  iban: string | null;
  ownerName: string | null;
  accountName: string | null;
  currency: string;
  isActive: boolean;
  /** Compte personnel : toutes ses transactions comptent au Perso, pas au Commun. */
  isPersonal: boolean;
}

export interface UpdateBankAccount {
  isActive?: boolean;
  isPersonal?: boolean;
}

export interface BankConnection {
  id: number;
  institutionId: string;
  institutionName: string;
  institutionLogo: string;
  status: BankConnectionStatus;
  createdAt: string;
  lastSyncAt: string | null;
  accounts: BankAccount[];
}

export interface CategoryRule {
  id: number;
  keyword: string;
  categoryId: number;
  categoryName: string;
  /** Les transactions matchées sont marquées charge fixe. */
  markAsFixed: boolean;
  /** Les dépenses carte Trade Republic matchées comptent au Perso, pas au Commun. */
  routeToPerso: boolean;
}

export interface CreateCategoryRule {
  keyword: string;
  categoryId: number;
  markAsFixed: boolean;
  routeToPerso: boolean;
}

export interface UpdateCategoryRule {
  keyword?: string;
  categoryId?: number;
  markAsFixed?: boolean;
  routeToPerso?: boolean;
}

export interface TradeRepublicLoginRequest {
  phoneNumber: string;
  pin: string;
}

export interface TradeRepublicLoginResponse {
  connectionId: number;
}

export interface TradeRepublicVerifyRequest {
  connectionId: number;
  code: string;
}
