export const InvestmentKind = {
  Security: 0,
  Metal: 1,
  InsuranceContract: 2,
} as const;
export type InvestmentKind = (typeof InvestmentKind)[keyof typeof InvestmentKind];

export const InvestmentUnit = {
  Share: 0,
  Gram: 1,
  Ounce: 2,
  Contract: 3,
} as const;
export type InvestmentUnit = (typeof InvestmentUnit)[keyof typeof InvestmentUnit];

export const InvestmentSource = {
  Manual: 0,
  TradeRepublic: 1,
} as const;
export type InvestmentSource = (typeof InvestmentSource)[keyof typeof InvestmentSource];

export const ValuationSource = {
  Manual: 0,
  TradeRepublic: 1,
  SpotApi: 2,
} as const;
export type ValuationSource = (typeof ValuationSource)[keyof typeof ValuationSource];

export interface Investment {
  id: number;
  dashboardId: number;
  name: string;
  holder: string;
  kind: InvestmentKind;
  isin: string | null;
  metalCode: string | null;
  quantity: number;
  unit: InvestmentUnit;
  costBasis: number;
  firstPurchaseDate: string | null;
  source: InvestmentSource;
  isArchived: boolean;
  createdAt: string;
  /** PRU. null pour un contrat d'assurance-vie. */
  unitCost: number | null;
  marketValue: number | null;
  valuationAsOf: string | null;
  isStale: boolean;
  gainAmount: number | null;
  gainPercent: number | null;
  /** null tant qu'aucune date d'entrée n'est renseignée. */
  annualizedReturn: number | null;
}

export interface InvestmentValuation {
  id: number;
  investmentId: number;
  asOf: string;
  unitPrice: number | null;
  marketValue: number;
  source: ValuationSource;
}

export interface CreateInvestment {
  dashboardId: number;
  name: string;
  holder: string;
  kind: InvestmentKind;
  isin?: string | null;
  metalCode?: string | null;
  quantity: number;
  unit: InvestmentUnit;
  costBasis: number;
  firstPurchaseDate?: string | null;
}

export interface UpdateInvestment {
  name?: string;
  holder?: string;
  isin?: string | null;
  metalCode?: string | null;
  quantity?: number;
  costBasis?: number;
  firstPurchaseDate?: string | null;
  isArchived?: boolean;
}

export interface CreateValuation {
  asOf: string;
  marketValue: number;
  unitPrice?: number | null;
}
