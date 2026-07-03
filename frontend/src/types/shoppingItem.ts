export interface ShoppingItem {
  id: number;
  dashboardId: number;
  label: string;
  estimatedCost?: number | null;
  isDone: boolean;
  createdAt: string;
}

export interface CreateShoppingItem {
  dashboardId: number;
  label: string;
  estimatedCost?: number | null;
}

export interface UpdateShoppingItem {
  label?: string;
  estimatedCost?: number | null;
  isDone?: boolean;
}
