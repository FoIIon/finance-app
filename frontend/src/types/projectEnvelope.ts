export interface ProjectEnvelope {
  id: number;
  dashboardId: number;
  name: string;
  icon: string;
  targetBudget?: number | null;
  fundingNote?: string | null;
  isArchived: boolean;
  createdAt: string;
}

export interface ProjectEnvelopeProgress {
  id: number;
  dashboardId: number;
  name: string;
  icon: string;
  targetBudget?: number | null;
  fundingNote?: string | null;
  isArchived: boolean;
  createdAt: string;
  /** Engagé = dépenses rattachées − remboursements rattachés. */
  spent: number;
  /** targetBudget − spent. null si pas de cible. */
  remaining?: number | null;
  transactionCount: number;
}

export interface CreateProjectEnvelope {
  dashboardId: number;
  name: string;
  icon?: string;
  targetBudget?: number | null;
  fundingNote?: string | null;
}

export interface UpdateProjectEnvelope {
  name?: string;
  icon?: string;
  targetBudget?: number | null;
  fundingNote?: string | null;
  isArchived?: boolean;
}
