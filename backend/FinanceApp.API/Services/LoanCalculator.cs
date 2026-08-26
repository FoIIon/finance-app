using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>Une échéance reconstruite, telle qu'elle apparaît sur un tableau d'amortissement.</summary>
public record LoanInstallment(
    DateTime DueDate,
    decimal Payment,
    decimal Interest,
    decimal Principal,
    decimal RemainingPrincipal);

/// <summary>Ce qu'il reste à devoir à une date donnée, et ce que ça coûtera jusqu'au bout.</summary>
public record LoanSummary(
    decimal RemainingPrincipal,
    int RemainingInstallments,
    DateTime? FinalDueDate,
    decimal RemainingInterest,
    decimal RemainingPayments,
    DateTime? NextDueDate,
    decimal? NextPayment);

/// <summary>
/// Reconstruit un tableau d'amortissement à partir d'un seul ancrage. La banque arrondit
/// l'intérêt au centime à chaque échéance avant d'en déduire l'amortissement : on fait pareil,
/// sinon le solde dérive de quelques centimes sur quinze ans.
/// </summary>
public static class LoanCalculator
{
    /// <summary>
    /// Borne de sécurité, dans les deux sens. Un emprunt qui ne s'éteint pas en cent ans
    /// ne s'éteindra jamais, et un ancrage à cette distance est une saisie erronée.
    /// On lève plutôt que de tronquer : un tableau tronqué annoncerait une date de
    /// libération fausse tout en devant encore du capital.
    /// </summary>
    private const int MaxInstallments = 1200;

    /// <summary>Taux mensuel proportionnel, la convention des crédits logement belges.</summary>
    public static decimal MonthlyRate(decimal annualRatePercent) => annualRatePercent / 100m / 12m;

    /// <summary>
    /// Indice de l'échéance, relatif à l'ancrage. Négatif pour une échéance antérieure.
    /// Les deux bornes sont ramenées à minuit : une heure traînant sur l'ancrage décalerait
    /// tout le tableau d'une échéance entière.
    /// </summary>
    private static int IndexOf(DateTime anchorDate, DateTime date)
    {
        var anchor = anchorDate.Date;
        var target = date.Date;
        var months = (target.Year - anchor.Year) * 12 + target.Month - anchor.Month;
        // AddMonths ramène le 31 sur un mois court : on recale plutôt que de faire confiance au calcul.
        while (anchor.AddMonths(months) > target) months--;
        while (anchor.AddMonths(months + 1) <= target) months++;
        return months;
    }

    /// <summary>Solde après l'échéance suivante, ou null si le prêt est déjà éteint.</summary>
    private static LoanInstallment? StepForward(Loan loan, decimal balance, DateTime dueDate)
    {
        if (balance <= 0m) return null;

        var interest = Math.Round(balance * MonthlyRate(loan.AnnualRatePercent), 2, MidpointRounding.AwayFromZero);
        var principal = loan.MonthlyPayment - interest;
        if (principal <= 0m)
            throw new InvalidOperationException(
                $"La mensualité de {loan.MonthlyPayment} ne couvre pas l'intérêt de {interest} : le capital ne diminuera jamais.");

        // Dernière échéance : elle solde le capital restant, quel qu'en soit le montant.
        if (principal >= balance)
            return new LoanInstallment(dueDate, balance + interest, interest, balance, 0m);

        return new LoanInstallment(dueDate, loan.MonthlyPayment, interest, principal, balance - principal);
    }

    /// <summary>
    /// Remonte d'une échéance. L'intérêt dépend du solde qu'on cherche, donc on itère :
    /// trois passes suffisent largement à converger au centime.
    /// </summary>
    private static decimal StepBackward(Loan loan, decimal balanceAfter)
    {
        var rate = MonthlyRate(loan.AnnualRatePercent);
        if (rate == 0m) return balanceAfter + loan.MonthlyPayment;

        var balance = (balanceAfter + loan.MonthlyPayment) / (1m + rate);
        for (var i = 0; i < 3; i++)
        {
            var interest = Math.Round(balance * rate, 2, MidpointRounding.AwayFromZero);
            balance = balanceAfter + loan.MonthlyPayment - interest;
        }
        return balance;
    }

    /// <summary>Indice de la dernière échéance tombée, et le capital restant dû juste après.</summary>
    private static (int Index, decimal Principal) PrincipalAndIndexAt(Loan loan, DateTime date)
    {
        var target = IndexOf(loan.AnchorDate, date);
        if (Math.Abs(target) > MaxInstallments)
            throw new InvalidOperationException(
                $"L'échéance de référence est à {Math.Abs(target)} mois de la date demandée, au-delà de la limite de {MaxInstallments}.");

        var anchor = loan.AnchorDate.Date;
        var balance = loan.AnchorPrincipal;

        if (target >= 0)
        {
            for (var k = 1; k <= target; k++)
            {
                var step = StepForward(loan, balance, anchor.AddMonths(k));
                if (step == null) return (target, 0m);
                balance = step.RemainingPrincipal;
            }
        }
        else
        {
            for (var k = 0; k > target; k--)
                balance = StepBackward(loan, balance);
        }

        return (target, Math.Round(balance, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>Capital restant dû après la dernière échéance tombée au plus tard à <paramref name="date"/>.</summary>
    public static decimal PrincipalAt(Loan loan, DateTime date) => PrincipalAndIndexAt(loan, date).Principal;

    /// <summary>Déroule le tableau à partir d'un point déjà calculé, jusqu'à extinction.</summary>
    private static List<LoanInstallment> BuildSchedule(Loan loan, int start, decimal balance)
    {
        var anchor = loan.AnchorDate.Date;
        var schedule = new List<LoanInstallment>();

        for (var k = start + 1; k <= start + MaxInstallments; k++)
        {
            var step = StepForward(loan, balance, anchor.AddMonths(k));
            if (step == null) return schedule;
            schedule.Add(step);
            balance = step.RemainingPrincipal;
        }

        throw new InvalidOperationException(
            $"L'emprunt ne s'éteint pas en {MaxInstallments} échéances : vérifier le taux et la mensualité.");
    }

    /// <summary>Échéances dues strictement après <paramref name="after"/>, jusqu'à extinction.</summary>
    public static IReadOnlyList<LoanInstallment> RemainingSchedule(Loan loan, DateTime after)
    {
        var (start, balance) = PrincipalAndIndexAt(loan, after);
        return BuildSchedule(loan, start, balance);
    }

    public static LoanSummary Summarize(Loan loan, DateTime asOf)
    {
        // Le capital à la date demandée coûte une remontée complète du tableau : on ne le
        // calcule qu'une fois, et l'échéancier repart de là.
        var (start, principal) = PrincipalAndIndexAt(loan, asOf);
        var schedule = BuildSchedule(loan, start, principal);
        var next = schedule.FirstOrDefault();

        return new LoanSummary(
            RemainingPrincipal: principal,
            RemainingInstallments: schedule.Count,
            FinalDueDate: schedule.Count > 0 ? schedule[^1].DueDate : null,
            RemainingInterest: schedule.Sum(i => i.Interest),
            RemainingPayments: schedule.Sum(i => i.Payment),
            NextDueDate: next?.DueDate,
            NextPayment: next?.Payment);
    }
}
