import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { echeancesApi } from '../../api/echeances';
import { useToast } from '../../hooks/useToast';
import type { Echeance } from '../../types/agenda';
import { addDays, formatShortDate } from '../agenda/agendaFormat';
import { Sheet } from './Sheet';
import {
  STRUCTURED_COMMUNICATION_HINT,
  amountToInput,
  fieldErrorsOf,
  formatIban,
  isValidStructuredCommunication,
  parseAmount,
  stripStructuredCommunication,
  todayIso,
  type FieldErrors,
} from './echeanceForm';

interface Props {
  dashboardId: number;
  /** Fournie : la feuille modifie cette échéance (PUT de remplacement). Absente : elle en crée une (POST). */
  initial?: Echeance;
  /** Pré-remplissage à la création, par exemple le nom d'un document déposé. */
  defaults?: { label?: string };
  onClose: () => void;
  onSaved?: (echeance: Echeance) => void;
}

interface FormData {
  label: string;
  dueDate: string;
  amount: number | null;
  counterpartyIban: string | null;
  structuredCommunication: string | null;
}

const inputClass =
  'w-full min-h-11 px-3 py-2.5 rounded-lg bg-white/5 border border-white/10 text-white text-sm placeholder-white/30 focus:outline-none focus:border-amber-500/50 disabled:opacity-50';
const errorClass = 'text-xs text-amber-300/90 mt-1';

/**
 * Libellé, date limite, montant facultatif, puis les deux clés du rapprochement automatique (lot 3), facultatives
 * aussi : l'IBAN du bénéficiaire et la communication structurée. Pas de notes (l'API les accepte, l'écran
 * attendra un besoin). Les refus du serveur s'écrivent sous le champ concerné. Après l'enregistrement,
 * l'agenda est invalidé et un toast dit pour quel jour.
 */
export const EcheanceFormSheet = ({ dashboardId, initial, defaults, onClose, onSaved }: Props) => {
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const editing = !!initial;

  const [label, setLabel] = useState(initial?.label ?? defaults?.label ?? '');
  const [dueDate, setDueDate] = useState(initial?.dueDate ?? addDays(todayIso(), 7));
  const [amount, setAmount] = useState(amountToInput(initial?.amount ?? null));
  const [counterpartyIban, setCounterpartyIban] = useState(initial?.counterpartyIban ? formatIban(initial.counterpartyIban) : '');
  const [structuredCommunication, setStructuredCommunication] = useState(initial?.structuredCommunication ?? '');
  const [errors, setErrors] = useState<FieldErrors>({});

  const save = useMutation({
    mutationFn: (data: FormData) =>
      initial
        ? echeancesApi.update(initial.id, { ...data, notes: initial.notes })
        : echeancesApi.create({ ...data, dashboardId, notes: null }),
    onSuccess: async (res) => {
      const saved = res.data;
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['agenda', dashboardId] }),
        queryClient.invalidateQueries({ queryKey: ['echeances', dashboardId] }),
        initial ? queryClient.invalidateQueries({ queryKey: ['echeance', initial.id] }) : Promise.resolve(),
      ]);
      showToast(editing ? 'Échéance modifiée' : `Échéance ajoutée pour le ${formatShortDate(saved.dueDate)}`, 'success');
      onSaved?.(saved);
      onClose();
    },
    onError: (err) => setErrors(fieldErrorsOf(err, editing ? 'Modification impossible, réessaie.' : 'Enregistrement impossible, réessaie.')),
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const next: FieldErrors = {};
    const trimmed = label.trim();
    if (!trimmed) next.label = 'Un libellé est nécessaire.';
    if (!dueDate) next.dueDate = 'Une date limite est nécessaire.';
    const parsed = parseAmount(amount);
    if (parsed === 'invalid') next.amount = 'Montant illisible, par exemple 12,50.';
    // La communication se vérifie ici, avant l'envoi, avec le même contrôle 97 que le serveur.
    const digits = stripStructuredCommunication(structuredCommunication);
    if (digits && !isValidStructuredCommunication(digits)) next.structuredCommunication = STRUCTURED_COMMUNICATION_HINT;
    setErrors(next);
    if (Object.keys(next).length || parsed === 'invalid') return;
    const iban = counterpartyIban.replace(/\s+/g, '');
    save.mutate({
      label: trimmed,
      dueDate,
      amount: parsed,
      counterpartyIban: iban ? iban : null,
      structuredCommunication: digits ? digits : null,
    });
  };

  return (
    <Sheet titleId="echeance-form-title" title={editing ? "Modifier l'échéance" : 'Nouvelle échéance'} onClose={onClose}>
      <form onSubmit={handleSubmit} className="space-y-4" noValidate>
        <div>
          <label htmlFor="echeance-label" className="block text-white/40 text-sm mb-1">Libellé</label>
          <input
            id="echeance-label"
            type="text"
            autoFocus
            autoCapitalize="sentences"
            autoComplete="off"
            maxLength={200}
            value={label}
            onChange={(e) => setLabel(e.target.value)}
            disabled={save.isPending}
            placeholder="Cotisation danse Alice"
            aria-invalid={!!errors.label}
            className={inputClass}
          />
          {errors.label && <p className={errorClass}>{errors.label}</p>}
        </div>

        <div>
          <label htmlFor="echeance-due-date" className="block text-white/40 text-sm mb-1">Date limite</label>
          <input
            id="echeance-due-date"
            type="date"
            value={dueDate}
            onChange={(e) => setDueDate(e.target.value)}
            disabled={save.isPending}
            aria-invalid={!!errors.dueDate}
            className={`${inputClass} [color-scheme:dark]`}
          />
          {errors.dueDate && <p className={errorClass}>{errors.dueDate}</p>}
        </div>

        <div>
          <label htmlFor="echeance-amount" className="block text-white/40 text-sm mb-1">
            Montant <span className="text-white/30">(facultatif)</span>
          </label>
          <input
            id="echeance-amount"
            type="text"
            inputMode="decimal"
            autoComplete="off"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            disabled={save.isPending}
            placeholder="62,40"
            aria-invalid={!!errors.amount}
            className={`${inputClass} tabular-nums`}
          />
          {errors.amount && <p className={errorClass}>{errors.amount}</p>}
        </div>

        <div>
          <label htmlFor="echeance-iban" className="block text-white/40 text-sm mb-1">
            IBAN du bénéficiaire <span className="text-white/30">(facultatif)</span>
          </label>
          <input
            id="echeance-iban"
            type="text"
            autoCapitalize="characters"
            autoComplete="off"
            spellCheck={false}
            maxLength={42}
            value={counterpartyIban}
            onChange={(e) => setCounterpartyIban(e.target.value)}
            disabled={save.isPending}
            placeholder="BE98 0682 4367 0693"
            aria-invalid={!!errors.counterpartyIban}
            className={`${inputClass} tabular-nums uppercase`}
          />
          {errors.counterpartyIban && <p className={errorClass}>{errors.counterpartyIban}</p>}
        </div>

        <div>
          <label htmlFor="echeance-communication" className="block text-white/40 text-sm mb-1">
            Communication structurée <span className="text-white/30">(facultatif)</span>
          </label>
          <input
            id="echeance-communication"
            type="text"
            inputMode="numeric"
            autoComplete="off"
            maxLength={20}
            value={structuredCommunication}
            onChange={(e) => setStructuredCommunication(e.target.value)}
            disabled={save.isPending}
            placeholder="+++123/4567/89002+++"
            aria-invalid={!!errors.structuredCommunication}
            className={`${inputClass} tabular-nums`}
          />
          {errors.structuredCommunication && <p className={errorClass}>{errors.structuredCommunication}</p>}
          <p className="text-xs text-white/40 mt-2">
            Avec l'IBAN et le montant, l'échéance passe payée toute seule quand le virement apparaît sur le compte.
          </p>
        </div>

        {errors.general && <p className="text-xs text-amber-300/90">{errors.general}</p>}

        <button
          type="submit"
          disabled={save.isPending}
          className="w-full min-h-11 rounded-xl bg-amber-500/20 text-amber-300 border border-amber-500/30 font-medium hover:bg-amber-500/30 disabled:opacity-50 transition-colors"
        >
          {save.isPending ? 'Enregistrement…' : 'Enregistrer'}
        </button>
      </form>
    </Sheet>
  );
};
