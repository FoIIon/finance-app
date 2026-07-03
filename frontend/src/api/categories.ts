import apiClient from './client';
import type { Category, CreateCategory, UpdateCategory } from '../types/category';

export const categoriesApi = {
  getAll: () =>
    apiClient.get<Category[]>('/category'),

  create: (data: CreateCategory) =>
    apiClient.post<Category>('/category', data),

  update: (id: number, data: UpdateCategory) =>
    apiClient.put<Category>(`/category/${id}`, data),

  setFixed: (id: number, isFixed: boolean) =>
    apiClient.put<Category>(`/category/${id}/fixed`, { isFixed }),

  delete: (id: number) =>
    apiClient.delete(`/category/${id}`),
};
