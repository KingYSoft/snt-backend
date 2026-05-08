using System;
using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto.MatchTransaction
{
    public class SaveMatchWriteOffInput
    {
        public string Mode { get; set; }
        public string BillingParty { get; set; }
        public string BillingPartyName { get; set; }
        public string MatchNumber { get; set; }
        public string Description { get; set; }
        public string BankPK { get; set; }
        public string BankAccountId { get; set; }
        public string BankAccountName { get; set; }
        public DateTime? SettleDate { get; set; }
        public string RefNo { get; set; }
        public string ChequeNo { get; set; }
        public decimal SettleAmount { get; set; }
        public List<MatchWriteOffLineInput> Lines { get; set; } = new();
    }

    public class MatchWriteOffLineInput
    {
        public string TthPk { get; set; }
    }
}
