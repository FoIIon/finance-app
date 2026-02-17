export const BankConnectionStatus = {
  Linked: 0,
  Expired: 1,
  Error: 2,
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
}

export interface CreateCategoryRule {
  keyword: string;
  categoryId: number;
}

export interface UpdateCategoryRule {
  keyword?: string;
  categoryId?: number;
}
