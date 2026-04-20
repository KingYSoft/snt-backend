using System.Collections.Generic;

namespace SntBackend.Application.Consolidation.Dto
{
    public class ConsolidationTblInput
    {
        public int SkipCount { get; set; }
        public int MaxResultCount { get; set; } = 20;
        public List<ConsolidationTblFilterItem> filters { get; set; } = new();
    }

    public class ConsolidationTblFilterItem
    {
        public string key { get; set; }
        public string op { get; set; }
        public string val { get; set; }
        public string start { get; set; }
        public string end { get; set; }
    }
}
