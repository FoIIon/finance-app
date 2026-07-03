export interface Category {
  id: number;
  name: string;
  icon: string;
  color: string;
  isDefault: boolean;
  isFixed: boolean;
}

export interface CreateCategory {
  name: string;
  icon: string;
  color: string;
  isFixed: boolean;
}

export interface UpdateCategory {
  name: string;
  icon: string;
  color: string;
  isFixed: boolean;
}
