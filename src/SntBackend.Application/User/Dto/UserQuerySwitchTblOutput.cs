using System.Collections.Generic;

namespace SntBackend.Application.User.Dto
{
    public class UserQuerySwitchTblOutput
    {
        public List<UserSwitchCompanyOutput> company_list { get; set; } = new List<UserSwitchCompanyOutput>();
    }

    public class UserSwitchCompanyOutput
    {
        public string company_pk { get; set; }
        public string company_code { get; set; }
        public string company_name { get; set; }
        public List<UserSwitchBranchOutput> branch_list { get; set; } = new List<UserSwitchBranchOutput>();
    }

    public class UserSwitchBranchOutput
    {
        public string branch_pk { get; set; }
        public string branch_code { get; set; }
        public string branch_name { get; set; }
        public List<UserSwitchDepartmentOutput> dept_list { get; set; } = new List<UserSwitchDepartmentOutput>();
    }

    public class UserSwitchDepartmentOutput
    {
        public string dept_pk { get; set; }
        public string dept_code { get; set; }
        public string dept_name { get; set; }
    }
}
