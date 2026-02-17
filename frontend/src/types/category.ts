export interface Category {
  id: number;
  name: string;
  icon: string;
  color: string;
  isDefault: boolean;
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
