using Abp.Application.Services.Dto;
using System.Collections.Generic;

namespace SntBackend.Application.SystemSettings.Dto
{
    public class SystemFilterItem
    {
        public string key { get; set; }
        public string op { get; set; }
        public string val { get; set; }
    }

    public abstract class SystemPagedQueryInput : PagedAndSortedResultRequestDto
    {
        public string query { get; set; }
        public List<SystemFilterItem> filters { get; set; } = new List<SystemFilterItem>();
    }

    public class CompanyQueryInput : SystemPagedQueryInput
    {
    }

    public class CompanySaveInput
    {
        public string pk { get; set; }
        public string code { get; set; }
        public string name { get; set; }
        public string business_reg_no { get; set; }
        public string business_reg_no2 { get; set; }
        public string customs_registration_no { get; set; }
        public string address1 { get; set; }
        public string address2 { get; set; }
        public string city { get; set; }
        public string phone { get; set; }
        public string postcode { get; set; }
        public string state { get; set; }
        public string fax { get; set; }
        public string email { get; set; }
        public string web_address { get; set; }
        public string home_currency { get; set; }
        public string country_code { get; set; }
        public int is_active { get; set; } = 1;
        public int is_valid { get; set; } = 1;
    }

    public class CompanyDtoOutput
    {
        public string pk { get; set; }
        public string code { get; set; }
        public string name { get; set; }
        public string business_reg_no { get; set; }
        public string business_reg_no2 { get; set; }
        public string customs_registration_no { get; set; }
        public string address1 { get; set; }
        public string address2 { get; set; }
        public string city { get; set; }
        public string phone { get; set; }
        public string postcode { get; set; }
        public string state { get; set; }
        public string fax { get; set; }
        public string email { get; set; }
        public string web_address { get; set; }
        public string home_currency { get; set; }
        public string country_code { get; set; }
        public int is_active { get; set; }
        public int is_valid { get; set; }
    }

    public class BranchQueryInput : SystemPagedQueryInput
    {
    }

    public class BranchSaveInput
    {
        public string pk { get; set; }
        public string code { get; set; }
        public string branch_name { get; set; }
        public string company_pk { get; set; }
        public string address1 { get; set; }
        public string address2 { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public string postcode { get; set; }
        public string phone { get; set; }
        public string fax { get; set; }
        public string email { get; set; }
        public string country_code { get; set; }
        public int is_active { get; set; } = 1;
        public int is_valid { get; set; } = 1;
    }

    public class BranchDtoOutput
    {
        public string pk { get; set; }
        public string code { get; set; }
        public string branch_name { get; set; }
        public string company_pk { get; set; }
        public string company_code { get; set; }
        public string company_name { get; set; }
        public string address1 { get; set; }
        public string address2 { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public string postcode { get; set; }
        public string phone { get; set; }
        public string fax { get; set; }
        public string email { get; set; }
        public string country_code { get; set; }
        public int is_active { get; set; }
        public int is_valid { get; set; }
    }

    public class BranchGetInput
    {
        public string pk { get; set; }
        public string code { get; set; }
    }

    public class BranchGroupListOutput
    {
        public List<GroupDtoOutput> list { get; set; } = new List<GroupDtoOutput>();
    }

    public class UserQueryInput : SystemPagedQueryInput
    {
    }

    public class UserSaveInput
    {
        public string pk { get; set; }
        public string code { get; set; }
        public string login_name { get; set; }
        public string full_name { get; set; }
        public string email_address { get; set; }
        public string work_phone { get; set; }
        public string mobile_phone { get; set; }
        public string home_branch { get; set; }
        public string home_department { get; set; }
        public string country_code { get; set; }
        public int is_active { get; set; } = 1;
        public int is_valid { get; set; } = 1;
        public int? can_login { get; set; }
        public string password { get; set; }
    }

    public class UserDtoOutput
    {
        public string pk { get; set; }
        public string code { get; set; }
        public string login_name { get; set; }
        public string full_name { get; set; }
        public string email_address { get; set; }
        public string work_phone { get; set; }
        public string mobile_phone { get; set; }
        public string home_branch { get; set; }
        public string home_department { get; set; }
        public string country_code { get; set; }
        public int is_active { get; set; }
        public int is_valid { get; set; }
        public int can_login { get; set; }
    }

    public class UserQueryAllOutput
    {
        public List<UserDtoOutput> list { get; set; } = new List<UserDtoOutput>();
    }

    public class GroupQueryInput : SystemPagedQueryInput
    {
    }

    public class GroupDtoOutput
    {
        public string pk { get; set; }
        public string code { get; set; }
        public string description { get; set; }
        public string desc { get; set; }
        public string is_admin { get; set; }
        public int is_active { get; set; }
    }

    public class GroupPermissionRowInput
    {
        public List<string> company_pks { get; set; } = new List<string>();
        public List<string> branch_pks { get; set; } = new List<string>();
        public List<string> dept_pks { get; set; } = new List<string>();
        public string is_allow { get; set; }
        public List<string> permission_names { get; set; } = new List<string>();
    }

    public class GroupSaveInput
    {
        public GroupDtoOutput group_detail { get; set; }
        public string pk { get; set; }
        public string code { get; set; }
        public string description { get; set; }
        public string desc { get; set; }
        public string is_admin { get; set; }
        public int is_active { get; set; } = 1;
        public List<string> selected_user_pks { get; set; } = new List<string>();
        public List<GroupPermissionRowInput> permission_rows { get; set; } = new List<GroupPermissionRowInput>();
    }

    public class GroupDetailOutput
    {
        public GroupDtoOutput group_detail { get; set; }
        public List<UserDtoOutput> users { get; set; } = new List<UserDtoOutput>();
        public List<GroupPermissionRowOutput> permission_rows { get; set; } = new List<GroupPermissionRowOutput>();
    }

    public class GroupPermissionRowOutput
    {
        public List<string> company_pks { get; set; } = new List<string>();
        public string company_code { get; set; }
        public string company_name { get; set; }
        public List<string> branch_pks { get; set; } = new List<string>();
        public string branch_code { get; set; }
        public string branch_name { get; set; }
        public List<string> dept_pks { get; set; } = new List<string>();
        public string dept_code { get; set; }
        public string dept_name { get; set; }
        public string is_allow { get; set; }
        public List<string> permission_names { get; set; } = new List<string>();
    }

    public class GroupSaveOutput
    {
        public string pk { get; set; }
        public GroupDtoOutput group { get; set; }
    }

    public class PermissionDtoOutput
    {
        public string name { get; set; }
        public string permission_name { get; set; }
        public string display_name { get; set; }
        public List<PermissionDtoOutput> children { get; set; } = new List<PermissionDtoOutput>();
    }

    public class GroupAllPermissionOutput
    {
        public List<PermissionDtoOutput> list { get; set; } = new List<PermissionDtoOutput>();
    }

    public class CompanyBranchDeptOptionsOutput
    {
        public List<CompanyOptionOutput> company_list { get; set; } = new List<CompanyOptionOutput>();
    }

    public class CompanyOptionOutput
    {
        public string pk { get; set; }
        public string code { get; set; }
        public string name { get; set; }
        public string company_code { get; set; }
        public string company_name { get; set; }
        public List<string> company_pks { get; set; } = new List<string>();
        public List<BranchOptionOutput> branch_list { get; set; } = new List<BranchOptionOutput>();
    }

    public class BranchOptionOutput
    {
        public string pk { get; set; }
        public string code { get; set; }
        public string name { get; set; }
        public string company_pk { get; set; }
        public string branch_code { get; set; }
        public string branch_name { get; set; }
        public List<string> branch_pks { get; set; } = new List<string>();
        public List<DeptOptionOutput> dept_list { get; set; } = new List<DeptOptionOutput>();
    }

    public class DeptOptionOutput
    {
        public string pk { get; set; }
        public string code { get; set; }
        public string name { get; set; }
        public string dept_code { get; set; }
        public string dept_name { get; set; }
        public List<string> dept_pks { get; set; } = new List<string>();
    }
}
