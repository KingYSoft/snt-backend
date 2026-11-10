using System.Collections.Generic;

namespace SntBackend.Application.Settlement.ClosingPeriod.Dto
{
    public class ClosingPeriodQueryInput
    {
        public int skipCount { get; set; }
        public int maxResultCount { get; set; } = 50;
        public List<ClosingPeriodFilter> filters { get; set; } = new();
    }

    public class ClosingPeriodFilter
    {
        public string key { get; set; }
        public string op { get; set; }
        public string val { get; set; }
        public string start { get; set; }
        public string end { get; set; }
    }

    public class ClosingPeriodSaveInput
    {
        public List<string> branch_pk_list { get; set; } = new();
        public string close_period { get; set; }
        public string closing_date { get; set; }
    }

    public class ClosingPeriodUpdateInput : ClosingPeriodSaveInput
    {
        public long id { get; set; }
    }

    public class ClosingPeriodRecord
    {
        public long id { get; set; }
        public string pk { get; set; }
        public string branch_pk { get; set; }
        public string branch_code { get; set; }
        public string branch_name { get; set; }
        public string branch_name_cn { get; set; }
        public string branch_name_en { get; set; }
        public string close_period { get; set; }
        public string closing_date { get; set; }
        public string create_user { get; set; }
        public System.DateTime? creation_time { get; set; }
        public string last_modify_user { get; set; }
        public System.DateTime? last_modification_time { get; set; }
    }
}
