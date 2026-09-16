using System;

namespace SntBackend.Application.Billing
{
    /// <summary>
    /// Shared billing rules used by the read and write paths.
    /// </summary>
    public static class BillingDataRules
    {
        public static decimal ResolveLocalAmount(decimal? localAmount, decimal? originalAmount, decimal? exchangeRate)
        {
            var local = localAmount ?? 0m;
            if (local != 0m || (originalAmount ?? 0m) == 0m)
                return local;

            var rate = exchangeRate.GetValueOrDefault();
            return (originalAmount ?? 0m) * (rate == 0m ? 1m : rate);
        }

        public static bool IsFullyPaid(decimal? outstandingAmount)
        {
            return Math.Abs(outstandingAmount ?? 0m) < 0.01m;
        }

        public static string SideAmountPredicate(string alias, string ledger)
        {
            var isAr = string.Equals(ledger, "AR", StringComparison.OrdinalIgnoreCase);
            var local = isAr ? "jr_localsellamt" : "jr_localcostamt";
            var original = isAr ? "jr_ossellamt" : "jr_oscostamt";
            var line = isAr ? "jr_al_arline" : "jr_al_apline";
            var party = isAr ? "jr_oh_sellaccount" : "jr_oh_costaccount";

            return $"( {alias}.{local} <> 0 OR {alias}.{original} <> 0 OR " +
                   $"{alias}.{line} IS NOT NULL OR {alias}.{party} IS NOT NULL )";
        }
    }
}
