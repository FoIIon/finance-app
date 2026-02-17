import apiClient from './client';
import type { LoginCredentials, RegisterCredentials, AuthResponse, MessageResponse } from '../types/auth';

export const authApi = {
  login: (credentials: LoginCredentials) =>
    apiClient.post<AuthResponse>('/auth/login', credentials),

  register: (credentials: RegisterCredentials) =>
    apiClient.post<MessageResponse>('/auth/register', credentials),

  confirmEmail: (token: string) =>
    apiClient.post<MessageResponse>('/auth/confirm-email', { token }),

  resendConfirmation: (email: string) =>
    apiClient.post<MessageResponse>('/auth/resend-confirmation', { email }),
};
