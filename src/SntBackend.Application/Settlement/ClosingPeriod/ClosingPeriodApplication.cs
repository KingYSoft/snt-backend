using Abp.Application.Services.Dto;
using Dapper;
using Facade;
using SntBackend.Application.Authorization;
using SntBackend.Application.Settlement.ClosingPeriod.Dto;
using SntBackend.DomainService.Share.App;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Settlement.ClosingPeriod
{
    public class ClosingPeriodApplication : SntBackendApplicationBase, IClosingPeriodApplication
    {
        private readonly IAppSqlServerRepository _repository;
        private readonly SystemAuditContextProvider _auditContextProvider;

        public ClosingPeriodApplication(
            IAppSqlServerRepository repository,
            SystemAuditContextProvider auditContextProvider)
        {
            _repository = repository;
            _auditContextProvider = auditContextProvider;
        }

        public async Task<PagedResultDto<ClosingPeriodRecord>> QueryPage(ClosingPeriodQueryInput input)
        {
            input ??= new ClosingPeriodQueryInput();
            var parameters = new DynamicParameters();
            var conditions = new List<string>();
            ApplyFilters(input.filters, conditions, parameters);

            var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
            var skip = Math.Max(0, input.skipCount);
            var take = input.maxResultCount <= 0 ? 50 : Math.Min(input.maxResultCount, 1000);
            parameters.Add("skip", skip);
            parameters.Add("take", take);

            var total = await _repository.QueryFirstOrDefaultAsync<int>($@"
SELECT COUNT(1)
FROM sun_period_manage p
INNER JOIN GlbBranch b ON b.GB_PK = p.branch_pk
{where}", parameters);

            var items = await _repository.QueryAsync<ClosingPeriodRecord>($@"
SELECT
    p.Id AS id,
    p.pk AS pk,
    p.branch_pk AS branch_pk,
    b.GB_Code AS branch_code,
    b.GB_BranchName AS branch_name,
    b.GB_BranchName AS branch_name_cn,
    b.GB_BranchName AS branch_name_en,
    p.close_period AS close_period,
    p.closing_date AS closing_date,
    p.create_user AS create_user,
    p.CreationTime AS creation_time,
    p.last_modify_user AS last_modify_user,
    p.LastModificationTime AS last_modification_time
FROM sun_period_manage p
INNER JOIN GlbBranch b ON b.GB_PK = p.branch_pk
{where}
ORDER BY p.close_period DESC, b.GB_Code DESC, p.Id DESC
OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", parameters);

            return new PagedResultDto<ClosingPeriodRecord>
            {
                TotalCount = total,
                Items = items.ToList()
            };
        }

        public async Task<List<ClosingPeriodRecord>> Create(ClosingPeriodSaveInput input)
        {
            ValidateInput(input);
            var closePeriod = NormalizeMonth(input.close_period);
            var closingDate = NormalizeDate(input.closing_date);
            var audit = await _auditContextProvider.GetAsync();
            var result = new List<ClosingPeriodRecord>();

            foreach (var branchPk in input.branch_pk_list
                         .Select(x => x?.Trim())
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await EnsureBranch(branchPk);
                var pk = Guid.NewGuid().ToString().ToUpperInvariant();
                var id = await _repository.QueryFirstOrDefaultAsync<long>(@"
INSERT INTO sun_period_manage
    (pk, close_period, closing_date, create_user, branch_pk, CreationTime)
VALUES
    (@pk, @close_period, @closing_date, @create_user, @branch_pk, GETUTCDATE());
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", new
                {
                    pk,
                    close_period = closePeriod,
                    closing_date = closingDate,
                    create_user = audit.UserCode,
                    branch_pk = branchPk
                });
                result.Add(await GetById(id));
            }

            return result;
        }

        public async Task<ClosingPeriodRecord> Update(ClosingPeriodUpdateInput input)
        {
            if (input == null || input.id <= 0)
                throw new AppException("Closing period record is required.");

            ValidateInput(input);
            var branchPk = input.branch_pk_list.FirstOrDefault()?.Trim();
            await EnsureBranch(branchPk);
            var audit = await _auditContextProvider.GetAsync();
            var affected = await _repository.ExecuteAsync(@"
UPDATE sun_period_manage
SET branch_pk = @branch_pk,
    close_period = @close_period,
    closing_date = @closing_date,
    last_modify_user = @last_modify_user,
    LastModificationTime = GETUTCDATE()
WHERE Id = @id", new
            {
                id = input.id,
                branch_pk = branchPk,
                close_period = NormalizeMonth(input.close_period),
                closing_date = NormalizeDate(input.closing_date),
                last_modify_user = audit.UserCode
            });

            if (affected == 0)
                throw new AppException("Closing period record was not found.");

            return await GetById(input.id);
        }

        private async Task EnsureBranch(string branchPk)
        {
            if (string.IsNullOrWhiteSpace(branchPk))
                throw new AppException("Business unit is required.");

            var exists = await _repository.QueryFirstOrDefaultAsync<string>(@"
SELECT TOP 1 GB_PK
FROM GlbBranch
WHERE GB_PK = @branch_pk
  AND GB_IsActive = 1
  AND GB_IsValid = 1", new { branch_pk = branchPk });

            if (string.IsNullOrWhiteSpace(exists))
                throw new AppException("Business unit was not found.");
        }

        private async Task<ClosingPeriodRecord> GetById(long id)
        {
            return await _repository.QueryFirstOrDefaultAsync<ClosingPeriodRecord>(@"
SELECT TOP 1
    p.Id AS id,
    p.pk AS pk,
    p.branch_pk AS branch_pk,
    b.GB_Code AS branch_code,
    b.GB_BranchName AS branch_name,
    b.GB_BranchName AS branch_name_cn,
    b.GB_BranchName AS branch_name_en,
    p.close_period AS close_period,
    p.closing_date AS closing_date,
    p.create_user AS create_user,
    p.CreationTime AS creation_time,
    p.last_modify_user AS last_modify_user,
    p.LastModificationTime AS last_modification_time
FROM sun_period_manage p
INNER JOIN GlbBranch b ON b.GB_PK = p.branch_pk
WHERE p.Id = @id", new { id });
        }

        private static void ApplyFilters(
            IEnumerable<ClosingPeriodFilter> filters,
            ICollection<string> conditions,
            DynamicParameters parameters)
        {
            var index = 0;
            foreach (var filter in filters ?? Enumerable.Empty<ClosingPeriodFilter>())
            {
                if (filter == null || string.IsNullOrWhiteSpace(filter.key))
                    continue;

                var key = filter.key.Trim().ToLowerInvariant();
                if (key == "branch_pk" && !string.IsNullOrWhiteSpace(filter.val))
                {
                    var parameter = $"branch_pk_{index++}";
                    conditions.Add($"p.branch_pk = @{parameter}");
                    parameters.Add(parameter, filter.val.Trim());
                }
                else if (key == "close_period")
                {
                    AddRange(conditions, parameters, "p.close_period", filter, true, ref index);
                }
                else if (key == "closing_date")
                {
                    AddRange(conditions, parameters, "p.closing_date", filter, false, ref index);
                }
            }
        }

        private static void AddRange(
            ICollection<string> conditions,
            DynamicParameters parameters,
            string column,
            ClosingPeriodFilter filter,
            bool month,
            ref int index)
        {
            if (!string.IsNullOrWhiteSpace(filter.start))
            {
                var parameter = $"range_start_{index++}";
                conditions.Add($"{column} >= @{parameter}");
                parameters.Add(parameter, month ? NormalizeMonth(filter.start) : NormalizeDate(filter.start));
            }
            if (!string.IsNullOrWhiteSpace(filter.end))
            {
                var parameter = $"range_end_{index++}";
                conditions.Add($"{column} <= @{parameter}");
                parameters.Add(parameter, month ? NormalizeMonth(filter.end) : NormalizeDate(filter.end));
            }
        }

        private static void ValidateInput(ClosingPeriodSaveInput input)
        {
            if (input == null || input.branch_pk_list == null || input.branch_pk_list.Count == 0)
                throw new AppException("Business unit is required.");
            NormalizeMonth(input.close_period);
            NormalizeDate(input.closing_date);
        }

        private static string NormalizeMonth(string value)
        {
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new AppException("Accounting month is invalid.");
            return date.ToString("yyyy-MM");
        }

        private static string NormalizeDate(string value)
        {
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new AppException("Closing date is invalid.");
            return date.ToString("yyyy-MM-dd");
        }
    }
}
