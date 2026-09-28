using Abp.Application.Services.Dto;
using Dapper;
using Facade;
using SntBackend.Application.Authorization;
using SntBackend.Application.SystemSettings;
using SntBackend.Application.SystemSettings.Dto;
using SntBackend.DomainService.Share.App;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.User
{
    public class UserApplication : SntBackendApplicationBase, IUserApplication
    {
        private readonly IAppSqlServerRepository _appSqlServerRepository;
        private readonly PasswordHashService _passwordHashService;
        private readonly SystemAuditContextProvider _auditContextProvider;

        public UserApplication(
            IAppSqlServerRepository appSqlServerRepository,
            PasswordHashService passwordHashService,
            SystemAuditContextProvider auditContextProvider)
        {
            _appSqlServerRepository = appSqlServerRepository;
            _passwordHashService = passwordHashService;
            _auditContextProvider = auditContextProvider;
        }

        public async Task<PagedResultDto<UserDtoOutput>> QueryPage(UserQueryInput input)
        {
            input ??= new UserQueryInput();
            var parameters = new DynamicParameters();
            var filters = input.filters ?? new List<SystemFilterItem>();
            var hasActiveFilter = filters.Any(x => string.Equals(x?.key, "is_active", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(x?.val));
            var hasValidFilter = filters.Any(x => string.Equals(x?.key, "is_valid", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(x?.val));
            var conditions = new List<string>();
            if (!hasActiveFilter)
                conditions.Add("gs_isactive = 1");
            if (!hasValidFilter)
                conditions.Add("gs_isvalid = 1");

            if (!string.IsNullOrWhiteSpace(input.query))
            {
                parameters.Add("search", $"%{input.query.Trim()}%");
                conditions.Add("(gs_loginname LIKE @search OR gs_fullname LIKE @search OR gs_emailaddress LIKE @search)");
            }

            var filterMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["pk"] = "gs_pk",
                ["code"] = "gs_code",
                ["login_name"] = "gs_loginname",
                ["full_name"] = "gs_fullname",
                ["email_address"] = "gs_emailaddress",
                ["work_phone"] = "gs_workphone",
                ["mobile_phone"] = "gs_mobilephone",
                ["home_branch"] = "gs_gb_homebranch",
                ["home_department"] = "gs_ge_homedepartment",
                ["is_active"] = "gs_isactive",
                ["is_valid"] = "gs_isvalid",
                ["can_login"] = "gs_canlogin"
            };
            var filterIndex = 0;
            foreach (var filter in filters)
            {
                if (filter == null || string.IsNullOrWhiteSpace(filter.val))
                    continue;

                if (!filterMap.TryGetValue(filter?.key ?? string.Empty, out var column))
                    throw new AppException($"Unsupported user filter: {filter?.key}");

                if (column == "gs_canlogin" || column == "gs_isactive" || column == "gs_isvalid")
                {
                    if (!int.TryParse(filter.val.Trim(), out var flag) || (flag != 0 && flag != 1))
                        throw new AppException($"{filter.key} filter must be 0 or 1.");

                    var op = string.IsNullOrWhiteSpace(filter.op) ? "Equal" : filter.op.Trim();
                    if (op != "Equal" && op != "Not Equal" && op != "NotEqual")
                        throw new AppException($"Unsupported {filter.key} filter operator: {op}");

                    var parameterName = $"filter_{filterIndex++}";
                    parameters.Add(parameterName, flag);
                    conditions.Add($"{column} {(op == "Equal" ? "=" : "<>")} @{parameterName}");
                    continue;
                }

                var condition = SystemSettingsQueryHelper.BuildTextFilter(
                    column, filter, parameters, $"filter_{filterIndex++}");
                if (!string.IsNullOrWhiteSpace(condition))
                    conditions.Add(condition);
            }

            var where = "WHERE " + string.Join(" AND ", conditions);
            var skip = SystemSettingsQueryHelper.NormalizeSkip(input.SkipCount);
            var limit = SystemSettingsQueryHelper.NormalizeLimit(input.MaxResultCount);
            parameters.Add("skip", skip);
            parameters.Add("limit", limit);

            var total = await _appSqlServerRepository.QueryFirstOrDefaultAsync<int>(
                $"SELECT COUNT(1) FROM GlbStaff {where}", parameters);
            var items = await _appSqlServerRepository.QueryAsync<UserDtoOutput>(@$"
SELECT
    gs_pk AS pk,
    gs_code AS code,
    gs_loginname AS login_name,
    gs_fullname AS full_name,
    gs_emailaddress AS email_address,
    gs_workphone AS work_phone,
    gs_mobilephone AS mobile_phone,
    gs_gb_homebranch AS home_branch,
    gs_ge_homedepartment AS home_department,
    gs_rn_nkcountrycode AS country_code,
    gs_isactive AS is_active,
    gs_isvalid AS is_valid,
    gs_canlogin AS can_login
FROM GlbStaff
{where}
ORDER BY gs_loginname, gs_pk
OFFSET @skip ROWS FETCH NEXT @limit ROWS ONLY", parameters);

            return new PagedResultDto<UserDtoOutput>
            {
                TotalCount = total,
                Items = items.ToList()
            };
        }

        public async Task<UserDtoOutput> Save(UserSaveInput input)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.login_name)
                || string.IsNullOrWhiteSpace(input.full_name))
                throw new AppException("Login name and full name are required.");

            var loginName = input.login_name.Trim();
            var duplicate = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(@"
SELECT TOP 1 gs_pk FROM GlbStaff
WHERE GS_LoginName = @login_name AND (@pk IS NULL OR GS_PK <> @pk)",
                new { login_name = loginName, pk = string.IsNullOrWhiteSpace(input.pk) ? null : input.pk.Trim() });
            if (!string.IsNullOrWhiteSpace(duplicate))
                throw new AppException("Login name already exists.");

            if (!string.IsNullOrWhiteSpace(input.home_branch))
            {
                var branch = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(@"
SELECT TOP 1 gb_pk FROM GlbBranch
WHERE gb_pk = @pk AND gb_isactive = 1 AND gb_isvalid = 1", new { pk = input.home_branch.Trim() });
                if (string.IsNullOrWhiteSpace(branch))
                    throw new AppException("Home branch not found or inactive.");
            }

            var pk = string.IsNullOrWhiteSpace(input.pk) ? SystemSettingsQueryHelper.NewPk() : input.pk.Trim();
            var password = string.IsNullOrWhiteSpace(input.password) ? null : _passwordHashService.Create(input.password);
            var audit = await _auditContextProvider.GetAsync();
            var parameters = new DynamicParameters();
            parameters.Add("pk", pk);
            parameters.Add("code", input.code);
            parameters.Add("login_name", loginName);
            parameters.Add("full_name", input.full_name.Trim());
            parameters.Add("email_address", input.email_address);
            parameters.Add("work_phone", input.work_phone);
            parameters.Add("mobile_phone", input.mobile_phone);
            parameters.Add("home_branch", input.home_branch);
            parameters.Add("home_department", input.home_department);
            parameters.Add("country_code", input.country_code);
            parameters.Add("is_active", input.is_active == 0 ? 0 : 1);
            parameters.Add("is_valid", input.is_valid == 0 ? 0 : 1);
            parameters.Add("can_login", input.can_login.GetValueOrDefault(1) == 0 ? 0 : 1);
            parameters.Add("password_hash", password?.Hash);
            parameters.Add("password_salt", password?.Salt);
            parameters.Add("password_iterations", password?.Iterations ?? 0);
            parameters.Add("system_create_user", audit.UserCode);
            parameters.Add("system_last_edit_user", audit.UserCode);
            parameters.Add("system_create_branch", audit.BranchCode);
            parameters.Add("system_create_department", audit.DepartmentCode);

            if (string.IsNullOrWhiteSpace(input.pk))
            {
                if (password == null)
                    throw new AppException("Password is required for a new user.");

                await _appSqlServerRepository.ExecuteAsync(@"
INSERT INTO GlbStaff
    (gs_pk, gs_isoperational, gs_changepasswordatnextlogin, gs_isactive,
     gs_iscontroller, gs_issystemaccount, gs_code, gs_loginname, gs_isvalid,
     gs_isresource, gs_resourcetype, gs_issalesrep, gs_isdeveloper,
     gs_employmentbasis, gs_nametitle, gs_fullname, gs_namesuffix,
     gs_friendlyname, gs_useraddress1, gs_useraddress2, gs_city, gs_state,
     gs_postcode, gs_gender, gs_title, gs_workphone, gs_publishworkphone,
     gs_workextension, gs_publishworkextension, gs_homephone, gs_publishhomephone,
     gs_mobilephone, gs_publishmobilephone, gs_faxnum, gs_publishfaxnum,
     gs_pager, gs_securitycardnumber, gs_enterprisecertificationid,
     gs_emailaddress, gs_publishemailaddress, gs_eftwages, gs_wagesbankname,
     gs_wagesbankaccount, gs_wagesbankbsb, gs_wagesbankswift,
     gs_nextofkinrelationship, gs_nextofkin, gs_nextofkinhomephone,
     gs_nextofkinworkphone, gs_emergencycontactrelationship,
     gs_emergencycontactname, gs_emergencyhomephone, gs_emergencyworkphone,
     gs_outontask, gs_personaledimailbox, gs_brokerid, gs_brokerpassword,
     gs_brokerworkingpassword, gs_brokerpasswordstatus, gs_passport,
     gs_isintrainingmode, gs_passwordneverchanges, gs_workinglanguage,
     gs_commissionbasis, gs_isactivitylogged, gs_systemcreateuser,
     gs_systemlastedituser, gs_canlogin, gs_isdevice,
     gs_istwofactorauthenticationenabled, gs_rn_nkcountrycode, gs_addressmap,
     gs_validationstatus, gs_rn_nknationalitycode, gs_domainname,
     gs_activitytrackingstatus, gs_isdriver, gs_passwordhash,
     gs_passwordsalt, gs_passwordhashiterations, gs_autoversion,
     gs_emergencycontactemail, gs_nextofkinemail, gs_residencystatus,
     gs_isrobot, gs_savepersonaldatatoactivedirectory, gs_externalid,
     gs_fullnameinmotherlanguage, gs_givenname, gs_middlename,
     gs_preferredsurname, gs_surname, gs_gendercustomterm,
     gs_samaccountfullname, gs_systemcreatebranch, gs_systemcreatedepartment)
VALUES
    (@pk, 1, 0, @is_active, 0, 0, @code, @login_name, @is_valid,
     0, '', 0, 0, '', '', @full_name, '', @full_name, '', '', '', '',
     '', '', '', @work_phone, 0, '', 0, '', 0, @mobile_phone, 0, '', 0,
     '', '', '', @email_address, 1, 0, '', '', '', '', '', '', '', '', '',
     '', '', '', '', '', '', '', '', '', '', 0, 0, 'ZH-CN', '', 1,
     @system_create_user, @system_last_edit_user, @can_login, 0, 0,
     @country_code, '', 'INV', '', '', '', 0, @password_hash, @password_salt,
     @password_iterations, 1, '', '', '', 0, 0, '', @full_name, @full_name,
     '', '', '', '', @full_name, @system_create_branch,
     @system_create_department)", parameters);
            }
            else
            {
                var sql = @"
UPDATE GlbStaff
SET gs_code = @code,
    gs_loginname = @login_name,
    gs_fullname = @full_name,
    gs_emailaddress = @email_address,
    gs_workphone = @work_phone,
    gs_mobilephone = @mobile_phone,
    gs_gb_homebranch = @home_branch,
    gs_ge_homedepartment = @home_department,
    gs_rn_nkcountrycode = @country_code,
    gs_isactive = @is_active,
    gs_isvalid = @is_valid";
                if (input.can_login.HasValue)
                {
                    sql += ",\n    gs_canlogin = @can_login";
                }
                if (password != null)
                {
                    sql += @",
    gs_passwordhash = @password_hash,
    gs_passwordsalt = @password_salt,
    gs_passwordhashiterations = @password_iterations";
                }
                sql += " WHERE gs_pk = @pk";
                var affected = await _appSqlServerRepository.ExecuteAsync(sql, parameters);
                if (affected == 0)
                    throw new AppException("User not found.");
            }

            return await Detail(pk);
        }

        public async Task Delete(string pk)
        {
            if (string.IsNullOrWhiteSpace(pk))
                throw new AppException("User primary key is required.");

            var affected = await _appSqlServerRepository.ExecuteAsync(
                "UPDATE GlbStaff SET gs_isactive = 0, gs_canlogin = 0 WHERE gs_pk = @pk", new { pk });
            if (affected == 0)
                throw new AppException("User not found.");
        }

        public async Task<UserDtoOutput> Detail(string pk)
        {
            if (string.IsNullOrWhiteSpace(pk))
                throw new AppException("User primary key is required.");

            var result = await _appSqlServerRepository.QueryFirstOrDefaultAsync<UserDtoOutput>(@"
SELECT TOP 1
    gs_pk AS pk, gs_code AS code, gs_loginname AS login_name,
    gs_fullname AS full_name, gs_emailaddress AS email_address,
    gs_workphone AS work_phone, gs_mobilephone AS mobile_phone,
    gs_gb_homebranch AS home_branch, gs_ge_homedepartment AS home_department,
    gs_rn_nkcountrycode AS country_code, gs_isactive AS is_active,
    gs_isvalid AS is_valid, gs_canlogin AS can_login
FROM GlbStaff WHERE gs_pk = @pk", new { pk });
            if (result == null)
                throw new AppException("User not found.");

            return result;
        }

        public async Task<UserQueryAllOutput> QueryAll(string query)
        {
            var parameters = new DynamicParameters();
            var where = "WHERE gs_isactive = 1 AND gs_isvalid = 1";
            if (!string.IsNullOrWhiteSpace(query))
            {
                parameters.Add("search", $"%{query.Trim()}%");
                where += " AND (gs_loginname LIKE @search OR gs_fullname LIKE @search)";
            }

            var items = await _appSqlServerRepository.QueryAsync<UserDtoOutput>($@"
SELECT TOP 500
    gs_pk AS pk, gs_code AS code, gs_loginname AS login_name,
    gs_fullname AS full_name, gs_emailaddress AS email_address,
    gs_workphone AS work_phone, gs_mobilephone AS mobile_phone,
    gs_gb_homebranch AS home_branch, gs_ge_homedepartment AS home_department,
    gs_rn_nkcountrycode AS country_code, gs_isactive AS is_active,
    gs_isvalid AS is_valid, gs_canlogin AS can_login
FROM GlbStaff {where}
ORDER BY gs_loginname, gs_pk", parameters);
            return new UserQueryAllOutput { list = items.ToList() };
        }
    }
}
