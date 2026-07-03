import apiClient from './client';
import type { ShoppingItem, CreateShoppingItem, UpdateShoppingItem } from '../types/shoppingItem';

export const shoppingItemsApi = {
  getAll: (dashboardId: number) =>
    apiClient.get<ShoppingItem[]>('/shoppingitem', { params: { dashboardId } }),

  create: (data: CreateShoppingItem) =>
    apiClient.post<ShoppingItem>('/shoppingitem', data),

  update: (id: number, data: UpdateShoppingItem) =>
    apiClient.put<ShoppingItem>(`/shoppingitem/${id}`, data),

  toggle: (id: number) =>
    apiClient.put<ShoppingItem>(`/shoppingitem/${id}/toggle`),

  delete: (id: number) =>
    apiClient.delete(`/shoppingitem/${id}`),
};
