using Abp.Runtime.Session;
using Dapper;
using Facade;
using SntBackend.Application.Edoc.Dto;
using SntBackend.DomainService.Folders;
using SntBackend.DomainService.Share.App;
using SntBackend.DomainService.Share.Authorization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Edoc
{
    public class EdocApplication : SntBackendApplicationBase, IEdocApplication
    {
        private readonly IAppSqlServerRepository _repository;
        private readonly IAppFolders _appFolders;
        private readonly IPrincipalAccessor _principalAccessor;

        public EdocApplication(
            IAppSqlServerRepository repository,
            IAppFolders appFolders,
            IPrincipalAccessor principalAccessor)
        {
            _repository = repository;
            _appFolders = appFolders;
            _principalAccessor = principalAccessor;
        }

        public async Task<EdocSearchOutput> Search(EdocSearchInput input)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.parent_table) || string.IsNullOrWhiteSpace(input.related_key))
                return new EdocSearchOutput();

            var parentTable = input.parent_table.Trim().ToUpperInvariant();
            if (parentTable is not ("SHP" or "CON"))
                throw new AppException("Unsupported parent_table.");

            var rows = await _repository.QueryAsync<EdocItemOutput>(@"
SELECT
    e.Id AS id,
    e.pk,
    e.parent_table,
    e.related_key,
    e.file_name,
    e.file_type,
    e.file_path,
    e.ref_no,
    e.remarks,
    e.is_ebooking_visible,
    e.is_forward AS is_publish,
    e.last_modify_user AS published_by,
    e.LastModificationTime AS published_time,
    e.received_time,
    e.create_user AS create_user_name,
    d.GE_Code AS dept_code,
    1 AS has_download,
    1 AS has_preview,
    0 AS has_gen_edi,
    e.is_gen_edi,
    e.gen_edi_time
FROM edoc e
LEFT JOIN GlbDepartment d ON d.GE_PK = e.department_pk
WHERE e.parent_table = @parentTable
  AND e.related_key = @relatedKey
  AND ISNULL(e.is_forward, 0) = 0
ORDER BY e.received_time DESC, e.Id DESC", new
            {
                parentTable,
                relatedKey = input.related_key.Trim()
            });

            return new EdocSearchOutput { list = rows.ToList() };
        }

        public async Task<EdocItemOutput> Save(
            EdocSaveInput input,
            Stream file,
            string fileName,
            string contentType,
            long fileLength)
        {
            if (input == null)
                throw new AppException("eDoc input is required.");
            if (file == null || fileLength <= 0)
                throw new AppException("File content can not be empty.");
            if (fileLength > 100 * 1024 * 1024)
                throw new AppException("File exceeds 100M.");
            if (string.IsNullOrWhiteSpace(input.file_type))
                throw new AppException("File Type can not null.");
            if (string.IsNullOrWhiteSpace(input.ref_no) && string.IsNullOrWhiteSpace(input.related_key))
                throw new AppException("Ref No can not null.");

            var parentTable = input.parent_table?.Trim().ToUpperInvariant();
            if (parentTable is not ("SHP" or "CON"))
                throw new AppException("Unsupported parent_table.");

            var relatedKey = await ResolveRelatedKey(parentTable, input.related_key, input.ref_no);
            var refNo = string.IsNullOrWhiteSpace(input.ref_no)
                ? relatedKey
                : input.ref_no.Trim();
            var safeFileName = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName is "." or "..")
                throw new AppException("File name is invalid.");

            var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/vnd.ms-excel",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/msword",
                "application/pdf",
                "image/jpeg",
                "image/png",
                "application/zip",
                "application/x-zip-compressed",
                "message/rfc822",
                "application/vnd.ms-outlook"
            };
            var extension = Path.GetExtension(safeFileName);
            if (!allowedTypes.Contains(contentType ?? string.Empty)
                && extension is not (".eml" or ".msg"))
                throw new AppException("file type incorrect.");

            var root = _appFolders.FileUploadFolder;
            if (string.IsNullOrWhiteSpace(root))
                throw new AppException("File upload folder is not configured.");

            var folder = Path.Combine(root, "edoc", parentTable, relatedKey);
            Directory.CreateDirectory(folder);
            var storedFileName = $"{Guid.NewGuid():N}_{safeFileName}";
            var fullPath = Path.Combine(folder, storedFileName);
            var relativePath = Path.GetRelativePath(root, fullPath).Replace('\\', '/');

            await using (var target = File.Create(fullPath))
            {
                await file.CopyToAsync(target);
            }

            var context = await GetAuditContext();
            var pk = Guid.NewGuid().ToString().ToUpperInvariant();
            try
            {
                await _repository.ExecuteAsync(@"
INSERT INTO edoc
(
    pk, parent_table, related_key, file_name, file_type, file_path, ref_no,
    received_time, company_pk, department_pk, branch_pk, file_type_code,
    file_type_name, is_ebooking_visible, is_forward, create_user, CreationTime
)
VALUES
(
    @pk, @parentTable, @relatedKey, @fileName, @fileType, @filePath, @refNo,
    SYSUTCDATETIME(), @companyPk, @departmentPk, @branchPk, @fileType,
    @fileType, @isEbookingVisible, 0, @createUser, SYSUTCDATETIME()
)", new
                {
                    pk,
                    parentTable,
                    relatedKey,
                    fileName = safeFileName,
                    fileType = input.file_type.Trim(),
                    filePath = relativePath,
                    refNo,
                    context.CompanyPk,
                    context.DepartmentPk,
                    context.BranchPk,
                    isEbookingVisible = input.is_ebooking_visible == 1 ? 1 : 0,
                    createUser = context.UserCode
                });
            }
            catch
            {
                File.Delete(fullPath);
                throw;
            }

            return (await Search(new EdocSearchInput { parent_table = parentTable, related_key = relatedKey }))
                .list.First(x => string.Equals(x.pk, pk, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<string> ResolveRelatedKey(string parentTable, string relatedKey, string refNo)
        {
            if (!string.IsNullOrWhiteSpace(relatedKey))
            {
                var exists = parentTable == "SHP"
                    ? await _repository.QueryFirstOrDefaultAsync<string>(@"
SELECT TOP 1 js_pk
FROM JobShipment
WHERE js_pk = @relatedKey
  AND js_iscancelled = 0", new { relatedKey = relatedKey.Trim() })
                    : await _repository.QueryFirstOrDefaultAsync<string>(@"
SELECT TOP 1 jk_pk
FROM JobConsol
WHERE jk_pk = @relatedKey
  AND jk_iscancelled = 0", new { relatedKey = relatedKey.Trim() });

                if (string.IsNullOrWhiteSpace(exists))
                    throw new AppException($"No record found for {parentTable}.");
                return exists;
            }

            var key = parentTable == "SHP"
                ? await _repository.QueryFirstOrDefaultAsync<string>(@"
SELECT TOP 1 js_pk
FROM JobShipment
WHERE js_uniqueconsignref = @refNo
  AND js_iscancelled = 0
ORDER BY js_pk DESC", new { refNo = refNo.Trim() })
                : await _repository.QueryFirstOrDefaultAsync<string>(@"
SELECT TOP 1 jk_pk
FROM JobConsol
WHERE jk_uniqueconsignref = @refNo
  AND jk_iscancelled = 0
ORDER BY jk_pk DESC", new { refNo = refNo.Trim() });

            if (string.IsNullOrWhiteSpace(key))
                throw new AppException($"No record found for {parentTable}.");
            return key;
        }

        private async Task<EdocAuditContext> GetAuditContext()
        {
            var claims = _principalAccessor.Principal?.Claims;
            var staffPk = claims?.FirstOrDefault(x => x.Type == SntClaimTypes.GlbStaffPk)?.Value;
            var companyPk = claims?.FirstOrDefault(x => x.Type == SntClaimTypes.CompanyPk)?.Value;
            var branchPk = claims?.FirstOrDefault(x => x.Type == SntClaimTypes.BranchPk)?.Value;
            var departmentPk = claims?.FirstOrDefault(x => x.Type == SntClaimTypes.DeptPk)?.Value;
            if (string.IsNullOrWhiteSpace(staffPk))
                throw new AppException("Current operator is required.");

            var context = await _repository.QueryFirstOrDefaultAsync<EdocAuditContext>(@"
SELECT TOP 1
    s.GS_Code AS UserCode,
    COALESCE(@companyPk, b.GB_GC) AS CompanyPk,
    COALESCE(@departmentPk, s.GS_GE_HomeDepartment) AS DepartmentPk,
    COALESCE(@branchPk, s.GS_GB_HomeBranch) AS BranchPk
FROM GlbStaff s
LEFT JOIN GlbBranch b ON b.GB_PK = COALESCE(@branchPk, s.GS_GB_HomeBranch)
WHERE s.GS_PK = @staffPk", new { staffPk, companyPk, branchPk, departmentPk });
            if (context == null || string.IsNullOrWhiteSpace(context.UserCode))
                throw new AppException("Current operator was not found.");

            return context;
        }

        private sealed class EdocAuditContext
        {
            public string UserCode { get; set; }
            public string CompanyPk { get; set; }
            public string DepartmentPk { get; set; }
            public string BranchPk { get; set; }
        }
    }
}
