using Abp.Application.Services.Dto;
using SntBackend.Application.Authorization;
using Dapper;
using Facade;
using SntBackend.Application.SystemSettings;
using SntBackend.Application.SystemSettings.Dto;
using SntBackend.DomainService.Share.App;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Branch
{
    public class BranchApplication : SntBackendApplicationBase, IBranchApplication
    {
        private readonly IAppSqlServerRepository _appSqlServerRepository;
        private readonly SystemAuditContextProvider _auditContextProvider;

        public BranchApplication(
            IAppSqlServerRepository appSqlServerRepository,
            SystemAuditContextProvider auditContextProvider)
        {
            _appSqlServerRepository = appSqlServerRepository;
            _auditContextProvider = auditContextProvider;
        }

        public async Task<PagedResultDto<BranchDtoOutput>> QueryPage(BranchQueryInput input)
        {
            input ??= new BranchQueryInput();
            var parameters = new DynamicParameters();
            var conditions = new List<string>
            {
                "b.gb_isactive = 1",
                "b.gb_isvalid = 1",
                "c.gc_isactive = 1",
                "c.gc_isvalid = 1"
            };

            if (!string.IsNullOrWhiteSpace(input.query))
            {
                parameters.Add("search", $"%{input.query.Trim()}%");
                conditions.Add("(b.gb_code LIKE @search OR b.gb_branchname LIKE @search OR c.gc_code LIKE @search OR c.gc_name LIKE @search)");
            }

            var filterMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["code"] = "b.gb_code",
                ["branch_name"] = "b.gb_branchname",
                ["company_pk"] = "b.gb_gc",
                ["company_code"] = "c.gc_code",
                ["address1"] = "b.gb_address1",
                ["address2"] = "b.gb_address2",
                ["city"] = "b.gb_city",
                ["state"] = "b.gb_state",
                ["postcode"] = "b.gb_postcode",
                ["phone"] = "b.gb_phone",
                ["email"] = "b.gb_email",
                ["country_code"] = "b.gb_rn_nkcountrycode"
            };
            var filterIndex = 0;
            foreach (var filter in input.filters ?? new List<SystemFilterItem>())
            {
                if (filter == null || string.IsNullOrWhiteSpace(filter.val))
                    continue;

                if (!filterMap.TryGetValue(filter?.key ?? string.Empty, out var column))
                    throw new AppException($"Unsupported branch filter: {filter?.key}");

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

            var total = await _appSqlServerRepository.QueryFirstOrDefaultAsync<int>($@"
SELECT COUNT(1)
FROM GlbBranch b
INNER JOIN GlbCompany c ON c.gc_pk = b.gb_gc
{where}", parameters);

            var items = await _appSqlServerRepository.QueryAsync<BranchDtoOutput>(@$"
SELECT
    b.gb_pk AS pk,
    b.gb_code AS code,
    b.gb_branchname AS branch_name,
    b.gb_gc AS company_pk,
    c.gc_code AS company_code,
    c.gc_name AS company_name,
    b.gb_address1 AS address1,
    b.gb_address2 AS address2,
    b.gb_city AS city,
    b.gb_state AS state,
    b.gb_postcode AS postcode,
    b.gb_phone AS phone,
    b.gb_fax AS fax,
    b.gb_email AS email,
    b.gb_rn_nkcountrycode AS country_code,
    b.gb_isactive AS is_active,
    b.gb_isvalid AS is_valid
FROM GlbBranch b
INNER JOIN GlbCompany c ON c.gc_pk = b.gb_gc
{where}
ORDER BY c.gc_code, b.gb_code, b.gb_pk
OFFSET @skip ROWS FETCH NEXT @limit ROWS ONLY", parameters);

            return new PagedResultDto<BranchDtoOutput>
            {
                TotalCount = total,
                Items = items.ToList()
            };
        }

        public async Task<BranchDtoOutput> Save(BranchSaveInput input)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.code)
                || string.IsNullOrWhiteSpace(input.branch_name)
                || string.IsNullOrWhiteSpace(input.company_pk))
                throw new AppException("Branch code, name and company are required.");

            var companyExists = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(@"
SELECT TOP 1 gc_pk FROM GlbCompany
WHERE gc_pk = @company_pk AND gc_isactive = 1 AND gc_isvalid = 1", new { company_pk = input.company_pk.Trim() });
            if (string.IsNullOrWhiteSpace(companyExists))
                throw new AppException("Company not found or inactive.");

            var duplicate = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(@"
SELECT TOP 1 gb_pk FROM GlbBranch
WHERE gb_gc = @company_pk AND gb_code = @code AND (@pk IS NULL OR gb_pk <> @pk)",
                new
                {
                    company_pk = input.company_pk.Trim(),
                    code = input.code.Trim(),
                    pk = string.IsNullOrWhiteSpace(input.pk) ? null : input.pk.Trim()
                });
            if (!string.IsNullOrWhiteSpace(duplicate))
                throw new AppException("Branch code already exists in the company.");

            var pk = string.IsNullOrWhiteSpace(input.pk) ? SystemSettingsQueryHelper.NewPk() : input.pk.Trim();
            var audit = await _auditContextProvider.GetAsync();
            var parameters = new DynamicParameters();
            parameters.Add("pk", pk);
            parameters.Add("code", input.code.Trim());
            parameters.Add("branch_name", input.branch_name.Trim());
            parameters.Add("company_pk", input.company_pk.Trim());
            parameters.Add("address1", input.address1);
            parameters.Add("address2", input.address2);
            parameters.Add("city", input.city);
            parameters.Add("state", input.state);
            parameters.Add("postcode", input.postcode);
            parameters.Add("phone", input.phone);
            parameters.Add("fax", input.fax);
            parameters.Add("email", input.email);
            parameters.Add("country_code", input.country_code);
            parameters.Add("is_active", input.is_active == 0 ? 0 : 1);
            parameters.Add("is_valid", input.is_valid == 0 ? 0 : 1);
            parameters.Add("system_user", audit.UserCode);

            if (string.IsNullOrWhiteSpace(input.pk))
            {
                await _appSqlServerRepository.ExecuteAsync(@"
INSERT INTO GlbBranch
    (gb_pk, gb_code, gb_branchname, gb_gc, gb_address1, gb_address2,
     gb_city, gb_state, gb_postcode, gb_phone, gb_fax, gb_email,
     gb_webaddress, gb_internalextension, gb_rl_nkhomeport, gb_localdoclanguage,
     gb_accountinggroupcode, gb_rn_nkcountrycode, gb_addressmap,
     gb_validationstatus, gb_autoversion, gb_systemcreateuser,
     gb_systemlastedituser, gb_isactive, gb_isvalid)
VALUES
    (@pk, @code, @branch_name, @company_pk, @address1, @address2,
     @city, @state, @postcode, @phone, @fax, @email,
     '', '', '', 'DEF', '', @country_code, '', 'MAN', 1, @system_user,
     @system_user, @is_active, @is_valid)", parameters);
            }
            else
            {
                var affected = await _appSqlServerRepository.ExecuteAsync(@"
UPDATE GlbBranch
SET gb_code = @code,
    gb_branchname = @branch_name,
    gb_gc = @company_pk,
    gb_address1 = @address1,
    gb_address2 = @address2,
    gb_city = @city,
    gb_state = @state,
    gb_postcode = @postcode,
    gb_phone = @phone,
    gb_fax = @fax,
    gb_email = @email,
    gb_rn_nkcountrycode = @country_code,
    gb_isactive = @is_active,
    gb_isvalid = @is_valid
WHERE gb_pk = @pk", parameters);
                if (affected == 0)
                    throw new AppException("Branch not found.");
            }

            return await GetByPk(new BranchGetInput { pk = pk });
        }

        public async Task Delete(string pk)
        {
            if (string.IsNullOrWhiteSpace(pk))
                throw new AppException("Branch primary key is required.");

            var userCount = await _appSqlServerRepository.QueryFirstOrDefaultAsync<int>(@"
SELECT COUNT(1) FROM GlbStaff
WHERE gs_gb_homebranch = @pk AND gs_isactive = 1 AND gs_isvalid = 1", new { pk });
            if (userCount > 0)
                throw new AppException("Branch is referenced by active users.");

            var affected = await _appSqlServerRepository.ExecuteAsync(
                "UPDATE GlbBranch SET gb_isactive = 0 WHERE gb_pk = @pk", new { pk });
            if (affected == 0)
                throw new AppException("Branch not found.");
        }

        public async Task<BranchDtoOutput> GetByPk(BranchGetInput input)
        {
            if (input == null || (string.IsNullOrWhiteSpace(input.pk) && string.IsNullOrWhiteSpace(input.code)))
                throw new AppException("Branch primary key or code is required.");

            var result = await _appSqlServerRepository.QueryFirstOrDefaultAsync<BranchDtoOutput>(@"
SELECT TOP 1
    b.gb_pk AS pk, b.gb_code AS code, b.gb_branchname AS branch_name,
    b.gb_gc AS company_pk, c.gc_code AS company_code, c.gc_name AS company_name,
    b.gb_address1 AS address1, b.gb_address2 AS address2, b.gb_city AS city,
    b.gb_state AS state, b.gb_postcode AS postcode, b.gb_phone AS phone,
    b.gb_fax AS fax, b.gb_email AS email, b.gb_rn_nkcountrycode AS country_code,
    b.gb_isactive AS is_active, b.gb_isvalid AS is_valid
FROM GlbBranch b
INNER JOIN GlbCompany c ON c.gc_pk = b.gb_gc
WHERE (@pk IS NULL OR b.gb_pk = @pk)
  AND (@code IS NULL OR b.gb_code = @code)",
                new
                {
                    pk = string.IsNullOrWhiteSpace(input.pk) ? null : input.pk.Trim(),
                    code = string.IsNullOrWhiteSpace(input.code) ? null : input.code.Trim()
                });
            if (result == null)
                throw new AppException("Branch not found.");

            return result;
        }

        public async Task<BranchGroupListOutput> GroupList()
        {
            var output = new BranchGroupListOutput();
            output.list = (await _appSqlServerRepository.QueryAsync<GroupDtoOutput>(@"
SELECT pk, code, [desc] AS description, [desc] AS [desc], is_admin, is_active
FROM SYS_GROUP
WHERE is_active = 1
ORDER BY code")).ToList();
            return output;
        }
    }
}
