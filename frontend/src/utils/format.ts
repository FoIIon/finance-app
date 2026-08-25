export const formatCurrency = (amount: number) =>
  new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'EUR' }).format(amount);

/** Un pourcentage en notation française : toFixed rend toujours un point décimal. */
export const formatPercent = (value: number, digits = 1) =>
  new Intl.NumberFormat('fr-FR', { minimumFractionDigits: digits, maximumFractionDigits: digits }).format(value);
