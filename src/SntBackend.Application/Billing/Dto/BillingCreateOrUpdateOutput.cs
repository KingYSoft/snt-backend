using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto
{
    public class FieldChangeRecord
    {
        public string FieldName { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
    }

    public class ChargeChangeLog
    {
        public string Pk { get; set; }

        /// <summary>Create / Update</summary>
        public string Action { get; set; }

        public List<FieldChangeRecord> Changes { get; set; } = new();
    }

    public class BillingCreateOrUpdateOutput
    {
        public List<ChargeChangeLog> ChangeLogs { get; set; } = new();
    }
}
