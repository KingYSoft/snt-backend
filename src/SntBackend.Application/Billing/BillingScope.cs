namespace SntBackend.Application.Billing
{
    /// <summary>
    /// 账单锚点作用域：账单能力挂在 shipment(JobShipment) 还是 合单(JobConsol) 上。
    ///
    /// snt 的账单链路是 锚点 → JobHeader(jh_parentid = 锚点pk, jh_parenttablecode = 'JS'/'JK')
    /// → JobCharge / AccTransactionHeader。除了锚点表名与三个列名，两个作用域的 SQL 完全一致，
    /// 所以把差异收敛到这里，BillingCore 一份实现给 shipment 与 合单 共用。
    /// </summary>
    public sealed class BillingScope
    {
        /// <summary>JobHeader.jh_parenttablecode 取值：'JS' / 'JK'</summary>
        public string ParentTableCode { get; }

        /// <summary>锚点表名：JobShipment / JobConsol</summary>
        public string ParentTable { get; }

        /// <summary>锚点主键列：js_pk / jk_pk</summary>
        public string ParentPkColumn { get; }

        /// <summary>锚点作废列：js_iscancelled / jk_iscancelled</summary>
        public string CancelledColumn { get; }

        /// <summary>锚点业务号列（发票号前缀来源）：js_uniqueconsignref / jk_uniqueconsignref</summary>
        public string ConsignRefColumn { get; }

        /// <summary>锚点在报错文案里的称呼</summary>
        public string DisplayName { get; }

        /// <summary>锚点主键在接口入参里的字段名（报错文案用）：shpPk / jkPk</summary>
        public string PkParamName { get; }

        private BillingScope(string parentTableCode, string parentTable, string parentPkColumn,
            string cancelledColumn, string consignRefColumn, string displayName, string pkParamName)
        {
            ParentTableCode = parentTableCode;
            ParentTable = parentTable;
            ParentPkColumn = parentPkColumn;
            CancelledColumn = cancelledColumn;
            ConsignRefColumn = consignRefColumn;
            DisplayName = displayName;
            PkParamName = pkParamName;
        }

        /// <summary>货运(shipment)作用域</summary>
        public static readonly BillingScope Shipment =
            new BillingScope("JS", "JobShipment", "js_pk", "js_iscancelled", "js_uniqueconsignref", "货运", "shpPk");

        /// <summary>合单(consolidation)作用域</summary>
        public static readonly BillingScope Consol =
            new BillingScope("JK", "JobConsol", "jk_pk", "jk_iscancelled", "jk_uniqueconsignref", "合单", "jkPk");

        /// <summary>是否合单作用域（合单的作业头允许按需创建，见 BillingCore.EnsureJobHeaderAsync）</summary>
        public bool IsConsol => ParentTableCode == "JK";

        /// <summary>按 jh_parenttablecode 反解作用域；未知值回落 shipment。</summary>
        public static BillingScope FromParentTableCode(string parentTableCode)
        {
            return string.Equals(parentTableCode?.Trim(), "JK", System.StringComparison.OrdinalIgnoreCase)
                ? Consol
                : Shipment;
        }
    }
}
