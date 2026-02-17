import apiClient from './client';
import type { LoginCredentials, RegisterCredentials, AuthResponse } from '../types/auth';

export const authApi = {
  login: (credentials: LoginCredentials) =>
    apiClient.post<AuthResponse>('/auth/login', credentials),

  register: (credentials: RegisterCredentials) =>
    apiClient.post<AuthResponse>('/auth/register', credentials),
};
