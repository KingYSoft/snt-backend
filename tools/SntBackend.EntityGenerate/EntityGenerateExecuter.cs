using Abp.Collections.Extensions;
using Abp.Dependency;
using Abp.Extensions;
using Abp.IO;
using Dapper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SntBackend.EntityGenerate
{
    public class EntityGenerateExecuter : ITransientDependency
    {
        private readonly Log _log;
        private readonly IAppRepository _appRepository;
        private readonly string tab = "    ";
        private readonly string db_name = Consts.DBName;
        private readonly string __namespace = Consts.Namespace;
        private readonly string __db_context = Consts.DbContext;

        public EntityGenerateExecuter(IAppRepository appRepository, Log log)
        {
            _log = log;
            _appRepository = appRepository;
        }
        public bool Run()
        {
            try
            {
                var hostConnStr = Consts.DefaultNameOrConnectionString;
                if (hostConnStr.IsNullOrWhiteSpace())
                {
                    _log.Write("没有配置数据库的连接字符串");
                    return false;
                }

                _log.Write("数据库字符串: " + hostConnStr);

                _log.Write("----------------------------------------------------------");
                _log.Write("确定要开始生成实体吗? (Y/N): ");
                var command = Console.ReadLine();
                if (!command.IsIn("Y", "y"))
                {
                    _log.Write("生成取消");
                    return false;
                }

                _log.Write("数据库开始生成实体...");
                var db_tables = new string[] { "JobShipment", "JobHeader", "JobConsol",
                    "JobConShipLink", "OrgHeader", "OrgAddress","AccTransactionHeader",
                    "AccTransactionLines","AccTransactionMatchLink","AccChargeCode",
                    "GlbCompany","GlbBranch","GlbStaff" };
                var tableSql = @"
select t.TABLE_NAME, g.value as TABLE_COMMENT
  from information_schema.TABLES t
  left join sys.extended_properties g
    on g.major_id = OBJECT_ID(t.TABLE_NAME)
   and g.minor_id = 0
 where t.TABLE_NAME IN @db_tables
   and t.TABLE_CATALOG = @db_name
";
                var sql = @$"
WITH
    tempCTE
    as
    (
        {tableSql}
    )
SELECT t.*
FROM tempCTE t;

WITH
    tempCTE
    as
    (
       {tableSql}
    )
select 
    c.TABLE_NAME,
    c.COLUMN_NAME,
    c.COLUMN_DEFAULT,
    c.IS_NULLABLE,
    c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH,
    g.value as COLUMN_COMMENT
from information_schema.COLUMNS c
    left join sys.extended_properties g
    on g.major_id = OBJECT_ID(c.TABLE_NAME)
        and g.minor_id = c.ORDINAL_POSITION
where c.TABLE_CATALOG = @db_name
    and c.TABLE_NAME IN ( SELECT t.TABLE_NAME FROM tempCTE t )
order by c.ORDINAL_POSITION;
";
                // var tables = new List<TableNameDto>();
                // var dicCols = new List<ColumnNameDto>();
                // using (var ggg = _appRepository.QueryMultipleAsync(sql, new { db_name }).GetAwaiter().GetResult())
                // {
                //     tables = ggg.Read<TableNameDto>().ToList();
                //     dicCols = ggg.Read<ColumnNameDto>().ToList();
                // }

                var tables = _appRepository.QueryAsync<TableNameDto>(tableSql, new { db_name, db_tables }).GetAwaiter().GetResult();
                var dicCols = new List<ColumnNameDto>();
                var tNames = tables.Select(x => x.table_name).ToArray();
                if (tNames.Length > 0)
                {
                    var l = _appRepository.QueryAsync<ColumnNameDto>(@"
select 
    c.TABLE_NAME,
    c.COLUMN_NAME,
    c.COLUMN_DEFAULT,
    c.IS_NULLABLE,
    c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH,
    g.value as COLUMN_COMMENT
from information_schema.COLUMNS c
    left join sys.extended_properties g
    on g.major_id = OBJECT_ID(c.TABLE_NAME)
        and g.minor_id = c.ORDINAL_POSITION
where c.TABLE_CATALOG = @db_name
    and c.TABLE_NAME IN @tbl_names
order by c.ORDINAL_POSITION
", new { db_name, tbl_names = tNames }).GetAwaiter().GetResult();
                    dicCols = l.AsList();
                }
                //                 var tables = (_appRepository.QueryAsync<TableNameDto>(@"
                // select t.TABLE_NAME, g.value as TABLE_COMMENT
                //   from information_schema.TABLES t
                //   left join sys.extended_properties g
                //     on g.major_id = OBJECT_ID(t.TABLE_NAME)
                //    and g.minor_id = 0
                //  where t.TABLE_NAME != '__EFMigrationsHistory'
                //    and t.TABLE_CATALOG = @db_name
                // ", new { db_name })).GetAwaiter().GetResult();

                //                 var dicCols = new Dictionary<string, List<ColumnNameDto>>();
                //                 foreach (var t in tables)
                //                 {
                //                     var cols_list = (_appRepository.QueryAsync<ColumnNameDto>(@"
                // select c.COLUMN_NAME,
                //        c.COLUMN_DEFAULT,
                //        c.IS_NULLABLE,
                //        c.DATA_TYPE,
                //        c.CHARACTER_MAXIMUM_LENGTH,
                //        g.value as COLUMN_COMMENT
                //   from information_schema.COLUMNS c
                //   left join sys.extended_properties g
                //     on g.major_id = OBJECT_ID(c.TABLE_NAME)
                //    and g.minor_id = c.ORDINAL_POSITION
                //  where c.TABLE_CATALOG = @db_name
                //    and c.TABLE_NAME = @table_name
                //  order by c.ORDINAL_POSITION
                // ", new { db_name, t.table_name })).GetAwaiter().GetResult();
                //                     dicCols.Add(t.table_name, cols_list.AsList());
                //                 }
                // 生成 Repository
                {
                    _log.Write($"开始生成：IPoRepository.generate.cs");
                    var projectName = $"{__namespace}.DomainService.Share";
                    var targetFolder = ContentDirectoryFinder.CalculateProjectFolder(projectName);

                    var folder = Path.Combine(targetFolder, "Repositories");
                    DirectoryHelper.CreateIfNotExists(folder);

                    var sb = GenerateFileHeader();
                    // using
                    sb.AppendLine("using Abp.Domain.Repositories;");
                    sb.AppendLine($"using {__namespace}.DomainService.Share.Po;");
                    sb.AppendLine("");

                    // namespace 
                    sb.AppendLine($"namespace {projectName}.Repositories");
                    sb.AppendLine("{");
                    foreach (var t in tables)
                    {
                        var interfaceName = $"I{TableNameToClassName(t.table_name)}Repository";
                        var cols = dicCols.Where(a => a.table_name == t.table_name).ToList();
                        if (cols.Count <= 0)
                        {
                            continue;
                        }
                        // var id = cols.FirstOrDefault(a => a.column_name.ToLower() == "id");
                        // if (id == default)
                        //     throw new Exception("没有ID主键");
                        // var idDataType = GetDataType(id.data_type, id.is_nullable);
                        var idDataType = "string";

                        sb.AppendLine($"{tab}/// <summary>");
                        sb.AppendLine($"{tab}/// {t.table_comment}");
                        sb.AppendLine($"{tab}/// {interfaceName}");
                        sb.AppendLine($"{tab}/// </summary>");
                        sb.AppendLine($"{tab}public partial interface {interfaceName} : IRepository<{TableNameToEntityName(t.table_name)}, {idDataType}>, Abp.Dependency.ITransientDependency");
                        sb.AppendLine($"{tab}" + "{");
                        sb.AppendLine($"{tab}" + "}");
                        sb.AppendLine("");
                    }

                    sb.AppendLine("}");
                    var entityContent = sb.ToString();
                    File.WriteAllText(Path.Combine(folder, "IPoRepository.generate.cs"), entityContent, Encoding.UTF8);
                    _log.Write("********************************************************");
                    _log.Write("");
                }
                {
                    _log.Write($"开始生成：PoRepository.generate.cs");
                    var projectName = $"{__namespace}.SqlServer";
                    var targetFolder = ContentDirectoryFinder.CalculateProjectFolder(projectName);

                    var folder = Path.Combine(targetFolder, "EntityFrameworkCore", "Repositories");
                    DirectoryHelper.CreateIfNotExists(folder);

                    var sb = GenerateFileHeader();

                    // using
                    sb.AppendLine("using Abp.EntityFrameworkCore;");
                    sb.AppendLine($"using {__namespace}.DomainService.Share.Po;");
                    sb.AppendLine($"using {__namespace}.DomainService.Share.Repositories;");
                    sb.AppendLine("");

                    //namespace
                    sb.AppendLine($"namespace {projectName}.EntityFrameworkCore.Repositories");
                    sb.AppendLine("{");

                    foreach (var t in tables)
                    {
                        var className = $"{TableNameToClassName(t.table_name)}Repository";
                        var cols = dicCols.Where(a => a.table_name == t.table_name).ToList();
                        if (cols.Count <= 0)
                        {
                            continue;
                        }
                        // var id = cols.FirstOrDefault(a => a.column_name.ToLower() == "id");
                        // if (id == default)
                        //     throw new Exception("没有ID主键");
                        // var idDataType = GetDataType(id.data_type, id.is_nullable);
                        var idDataType = "string";

                        sb.AppendLine($"{tab}/// <summary>");
                        sb.AppendLine($"{tab}/// {t.table_comment}");
                        sb.AppendLine($"{tab}/// {className}");
                        sb.AppendLine($"{tab}/// </summary>");
                        sb.AppendLine($"{tab}public partial class {className} : SqlServerEfCoreRepositoryBase<{TableNameToEntityName(t.table_name)}, {idDataType}>, I{className}");
                        sb.AppendLine($"{tab}" + "{");
                        sb.AppendLine($"{tab}{tab}public {className}(IDbContextProvider<{__db_context}> dbContextProvider) : base(dbContextProvider)");
                        sb.AppendLine($"{tab}{tab}" + "{");
                        sb.AppendLine($"{tab}{tab}" + "}");
                        sb.AppendLine($"{tab}" + "}");
                        sb.AppendLine("");
                    }

                    sb.AppendLine("}");
                    var entityContent = sb.ToString();
                    File.WriteAllText(Path.Combine(folder, "PoRepository.generate.cs"), entityContent, Encoding.UTF8);

                    _log.Write("********************************************************");
                    _log.Write("");
                }


                // var excludeCol = new string[] { "Password", "Id", "CreationTime", "CreatorUserId", "LastModificationTime", "LastModifierUserId", "IsDeleted", "DeleterUserId", "DeletionTime" };
                var excludeCol = new string[] { };
                // 实体
                {
                    _log.Write($"开始生成：Po.generate.cs");
                    var projectName = $"{__namespace}.DomainService.Share";
                    var targetFolder = ContentDirectoryFinder.CalculateProjectFolder(projectName);
                    var folder = Path.Combine(targetFolder, "Po");
                    DirectoryHelper.CreateIfNotExists(folder);

                    var sb = GenerateFileHeader();

                    // using
                    sb.AppendLine($"using System;");
                    sb.AppendLine($"using Abp.Domain.Entities;");
                    sb.AppendLine("");

                    //namespace
                    sb.AppendLine("");
                    sb.AppendLine($"namespace {projectName}.Po");
                    sb.AppendLine("{");

                    foreach (var t in tables)
                    {
                        var tableName = TableNameToEntityName(t.table_name);
                        var className = $"{tableName}";
                        var cols = dicCols.Where(a => a.table_name == t.table_name).ToList();
                        if (cols.Count <= 0)
                        {
                            continue;
                        }
                        // var id = cols.FirstOrDefault(a => a.column_name.ToLower() == "id");
                        // if (id == default)
                        //     throw new Exception("没有ID主键");
                        // var idDataType = GetDataType(id.data_type, id.is_nullable);
                        var idDataType = "string";

                        sb.AppendLine($"{tab}/// <summary>");
                        sb.AppendLine($"{tab}/// {className}");
                        sb.AppendLine($"{tab}/// </summary>");
                        sb.AppendLine($"{tab}public partial class {className} : Entity<{idDataType}>");
                        sb.AppendLine($"{tab}" + "{");
                        foreach (var c in cols)
                        {
                            if (excludeCol.Any(x => x.Equals(c.column_name, StringComparison.OrdinalIgnoreCase)))
                                continue;

                            sb.AppendLine($"{tab}{tab}/// <summary>");
                            sb.AppendLine($"{tab}{tab}/// {c.column_comment}");
                            sb.AppendLine($"{tab}{tab}/// </summary>");
                            var dataType = GetDataType(c.data_type, c.is_nullable);
                            sb.AppendLine($"{tab}{tab}public " + $"{dataType}" + $" {c.column_name.ToLower()} " + "{ get; set; }");
                        }
                        sb.AppendLine($"{tab}" + "}");
                        sb.AppendLine("");
                    }

                    sb.AppendLine("}");
                    var str = sb.ToString();
                    File.WriteAllText(Path.Combine(folder, $"Po.generate.cs"), str, Encoding.UTF8);
                    _log.Write("********************************************************");
                    _log.Write("");
                }
                {
                    _log.Write($"开始生成：PoDtoOutput.generate.cs");
                    var projectName = $"{__namespace}.Application";
                    var targetFolder = ContentDirectoryFinder.CalculateProjectFolder(projectName);
                    var folder = Path.Combine(targetFolder, "Generates", "Dto");
                    DirectoryHelper.CreateIfNotExists(folder);

                    var sb = GenerateFileHeader();

                    // using
                    sb.AppendLine($"using Abp.Application.Services.Dto;");
                    sb.AppendLine($"using System;");
                    sb.AppendLine("");

                    //namespace
                    sb.AppendLine("");
                    sb.AppendLine($"namespace {projectName}.Po.Dto");
                    sb.AppendLine("{");

                    foreach (var t in tables)
                    {
                        var tableName = TableNameToClassName(t.table_name);
                        var className = $"{tableName}DtoOutput";
                        var cols = dicCols.Where(a => a.table_name == t.table_name).ToList();
                        if (cols.Count <= 0)
                        {
                            continue;
                        }
                        // var id = cols.FirstOrDefault(a => a.column_name.ToLower() == "id");
                        // if (id == default)
                        //     throw new Exception("没有ID主键");
                        // var idDataType = GetDataType(id.data_type, id.is_nullable);
                        var idDataType = "string";

                        sb.AppendLine($"{tab}/// <summary>");
                        sb.AppendLine($"{tab}/// 实体输出（AutoMapper）");
                        sb.AppendLine($"{tab}/// {className}");
                        sb.AppendLine($"{tab}/// </summary>");
                        sb.AppendLine($"{tab}public partial class {className} : EntityDto<{idDataType}>");
                        sb.AppendLine($"{tab}" + "{");
                        foreach (var c in cols)
                        {
                            if (excludeCol.Any(x => x.Equals(c.column_name, StringComparison.OrdinalIgnoreCase)))
                                continue;

                            sb.AppendLine($"{tab}{tab}/// <summary>");
                            sb.AppendLine($"{tab}{tab}/// {c.column_comment}");
                            sb.AppendLine($"{tab}{tab}/// </summary>");
                            var dataType = GetDataType(c.data_type, c.is_nullable);
                            sb.AppendLine($"{tab}{tab}public " + $"{dataType}" + $" {c.column_name.ToLower()} " + "{ get; set; }");
                        }
                        sb.AppendLine($"{tab}" + "}");
                        sb.AppendLine("");
                    }

                    sb.AppendLine("}");
                    var str = sb.ToString();
                    File.WriteAllText(Path.Combine(folder, $"PoDtoOutput.generate.cs"), str, Encoding.UTF8);
                    _log.Write("********************************************************");
                    _log.Write("");
                }

                {
                    _log.Write($"开始生成：PoCreateUpdateDtoInput.generate.cs");
                    var projectName = $"{__namespace}.Application";
                    var targetFolder = ContentDirectoryFinder.CalculateProjectFolder(projectName);
                    var folder = Path.Combine(targetFolder, "Generates", "Dto");
                    DirectoryHelper.CreateIfNotExists(folder);

                    var sb = GenerateFileHeader();

                    // using
                    sb.AppendLine($"using Abp.Application.Services.Dto;");
                    sb.AppendLine($"using System;");
                    sb.AppendLine("");

                    //namespace
                    sb.AppendLine("");
                    sb.AppendLine($"namespace {projectName}.Po.Dto");
                    sb.AppendLine("{");

                    foreach (var t in tables)
                    {
                        var tableName = TableNameToClassName(t.table_name);
                        var className = $"{tableName}CreateUpdateDtoInput";
                        var cols = dicCols.Where(a => a.table_name == t.table_name).ToList();
                        if (cols.Count <= 0)
                        {
                            continue;
                        }
                        // var id = cols.FirstOrDefault(a => a.column_name.ToLower() == "id");
                        // if (id == default)
                        //     throw new Exception("没有ID主键");
                        // var idDataType = GetDataType(id.data_type, id.is_nullable);
                        var idDataType = "string";

                        sb.AppendLine($"{tab}/// <summary>");
                        sb.AppendLine($"{tab}/// {className}");
                        sb.AppendLine($"{tab}/// </summary>");
                        sb.AppendLine($"{tab}public partial class {className} : EntityDto<{idDataType}>");
                        sb.AppendLine($"{tab}" + "{");
                        foreach (var c in cols)
                        {
                            if (excludeCol.Any(x => x.Equals(c.column_name, StringComparison.OrdinalIgnoreCase)))
                                continue;

                            sb.AppendLine($"{tab}{tab}/// <summary>");
                            sb.AppendLine($"{tab}{tab}/// {c.column_comment}");
                            sb.AppendLine($"{tab}{tab}/// </summary>");
                            var dataType = GetDataType(c.data_type, c.is_nullable);
                            sb.AppendLine($"{tab}{tab}public " + $"{dataType}" + $" {c.column_name.ToLower()} " + "{ get; set; }");
                        }
                        sb.AppendLine($"{tab}" + "}");
                        sb.AppendLine("");
                    }

                    sb.AppendLine("}");
                    var str = sb.ToString();
                    File.WriteAllText(Path.Combine(folder, $"PoCreateUpdateDtoInput.generate.cs"), str, Encoding.UTF8);
                    _log.Write("********************************************************");
                    _log.Write("");
                }


                {
                    _log.Write($"开始生成：PoMapProfile.generate.cs");
                    var projectName = $"{__namespace}.Application";
                    var targetFolder = ContentDirectoryFinder.CalculateProjectFolder(projectName);
                    var folder = Path.Combine(targetFolder, "Generates", "Dto");
                    DirectoryHelper.CreateIfNotExists(folder);

                    var sb = GenerateFileHeader();
                    // using
                    sb.AppendLine("using AutoMapper;");
                    sb.AppendLine($"using {__namespace}.DomainService.Share.Po;");
                    sb.AppendLine("using System;");
                    sb.AppendLine("");

                    //namespace
                    sb.AppendLine($"namespace {projectName}.Po.Dto");
                    sb.AppendLine("{");
                    foreach (var t in tables)
                    {
                        var tableName = TableNameToClassName(t.table_name);
                        var className = $"{tableName}MapProfile";

                        sb.AppendLine($"{tab}/// <summary>");
                        sb.AppendLine($"{tab}/// AutoMapper Profile 实体映射文件");
                        sb.AppendLine($"{tab}/// {className}");
                        sb.AppendLine($"{tab}/// </summary>");
                        sb.AppendLine($"{tab}public partial class {className} : Profile");
                        sb.AppendLine($"{tab}" + "{");
                        sb.AppendLine($"{tab}{tab}" + $"public {className}()");
                        sb.AppendLine($"{tab}{tab}" + "{");
                        sb.AppendLine($"{tab}{tab}{tab}" + $"CreateMap<{tableName}CreateUpdateDtoInput, {TableNameToEntityName(t.table_name)}>();");
                        sb.AppendLine($"{tab}{tab}{tab}" + $"CreateMap<{TableNameToEntityName(t.table_name)}, {tableName}DtoOutput>();");
                        sb.AppendLine($"{tab}{tab}" + "}");
                        sb.AppendLine($"{tab}" + "}");
                    }

                    sb.AppendLine("}");
                    var str = sb.ToString();
                    File.WriteAllText(Path.Combine(folder, $"PoMapProfile.generate.cs"), str, Encoding.UTF8);
                    _log.Write("********************************************************");
                    _log.Write("");
                }

                // {
                //     var fileName = $"PoDapperMap";
                //     _log.Write($"开始生成：{fileName}.generate.cs");
                //     var projectName = $"{__namespace}.DomainService.Share";

                //     var targetFolder = ContentDirectoryFinder.CalculateProjectFolder(projectName);
                //     var folder = Path.Combine(targetFolder, "Po");
                //     DirectoryHelper.CreateIfNotExists(folder);

                //     var sb = GenerateFileHeader();

                //     // using
                //     sb.AppendLine("using Facade.Dapper;");
                //     sb.AppendLine("using Facade.Dapper.Extensions.Mapper;");
                //     sb.AppendLine("");
                //     //spacename
                //     sb.AppendLine($"namespace {projectName}.Po");
                //     sb.AppendLine("{");
                //     foreach (var t in tables)
                //     {
                //         var tableName = TableNameToClassName(t.table_name);
                //         var cols = dicCols.Where(a => a.table_name == t.table_name).ToList();

                //         sb.AppendLine($"{tab}/// <summary>");
                //         sb.AppendLine($"{tab}/// {tableName}");
                //         sb.AppendLine($"{tab}/// </summary>");
                //         sb.AppendLine($"{tab}public sealed class {tableName}Map : DapperAutoClassMapper<{TableNameToEntityName(t.table_name)}>");
                //         sb.AppendLine($"{tab}" + "{");
                //         sb.AppendLine($"{tab}{tab}protected override void CustomMap()");
                //         sb.AppendLine($"{tab}{tab}" + "{");
                //         sb.AppendLine($"{tab}{tab}{tab}" + $"Map(x => x.Id).SetKeyType(KeyType.Identity);");
                //         sb.AppendLine($"{tab}{tab}{tab}" + $"base.CustomMap();");
                //         sb.AppendLine($"{tab}{tab}" + "}");
                //         sb.AppendLine($"{tab}" + "}");
                //         sb.AppendLine("");
                //     }
                //     sb.AppendLine("}");
                //     var str = sb.ToString();
                //     File.WriteAllText(Path.Combine(folder, $"{fileName}.generate.cs"), str, Encoding.UTF8);
                //     _log.Write("********************************************************");
                //     _log.Write("");
                // }
                {
                    // 生成 IRepositoryService.generate.cs 文件
                    // 位置 {__namespace}.DomainService.Generates.IRepositoryService.generate.cs
                    _log.Write($"开始生成：IRepositoryService.generate.cs");
                    var projectName = $"{__namespace}.DomainService";
                    var targetFolder = ContentDirectoryFinder.CalculateProjectFolder(projectName);
                    var folder = Path.Combine(targetFolder, "Generates"); //Generates 文件夹
                    DirectoryHelper.CreateIfNotExists(folder);

                    var sb = GenerateFileHeader();

                    // using
                    sb.AppendLine($"using {__namespace}.DomainService.Share.Repositories;");
                    sb.AppendLine("");
                    //spacename
                    sb.AppendLine($"namespace {__namespace}.DomainService.Repositories");
                    sb.AppendLine("{");
                    sb.AppendLine($"{tab}/// <summary>");
                    sb.AppendLine($"{tab}/// 实体的仓储服务");
                    sb.AppendLine($"{tab}/// IRepositoryService");
                    sb.AppendLine($"{tab}/// </summary>");
                    sb.AppendLine($"{tab}public partial interface IRepositoryService");
                    sb.AppendLine($"{tab}" + "{");

                    foreach (var t in tables)
                    {
                        var tableName = TableNameToClassName(t.table_name);
                        sb.AppendLine($"{tab}{tab}/// <summary>");
                        sb.AppendLine($"{tab}{tab}/// </summary>");
                        sb.AppendLine($"{tab}{tab}" + $"I{tableName}Repository" + $" {tableName}Repository " + "{ get; }");
                    }

                    sb.AppendLine($"{tab}" + "}");
                    sb.AppendLine("}");
                    var str = sb.ToString();
                    File.WriteAllText(Path.Combine(folder, $"IRepositoryService.generate.cs"), str, Encoding.UTF8);
                    _log.Write("********************************************************");
                    _log.Write("");
                }

                {
                    // 生成 RepositoryService.generate.cs 文件
                    // 位置 {__namespace}.DomainService.Generates.RepositoryService.generate.cs
                    _log.Write($"开始生成：RepositoryService.generate.cs");
                    var projectName = $"{__namespace}.DomainService";
                    var targetFolder = ContentDirectoryFinder.CalculateProjectFolder(projectName);
                    var folder = Path.Combine(targetFolder, "Generates"); //Generates 文件夹
                    DirectoryHelper.CreateIfNotExists(folder);

                    var sb = GenerateFileHeader();
                    // using
                    sb.AppendLine($"using {__namespace}.DomainService.Share.Repositories;");
                    sb.AppendLine("using Abp.Dependency;");
                    sb.AppendLine("");

                    //namespace
                    sb.AppendLine($"namespace {__namespace}.DomainService.Repositories");
                    sb.AppendLine("{");
                    sb.AppendLine($"{tab}/// <summary>");
                    sb.AppendLine($"{tab}/// 实体的仓储服务");
                    sb.AppendLine($"{tab}/// RepositoryService");
                    sb.AppendLine($"{tab}/// </summary>");
                    sb.AppendLine($"{tab}public partial class RepositoryService: IRepositoryService, ISingletonDependency");
                    sb.AppendLine($"{tab}" + "{");

                    foreach (var t in tables)
                    {
                        var tableName = TableNameToClassName(t.table_name);

                        sb.AppendLine($"{tab}{tab}/// <summary>");
                        sb.AppendLine($"{tab}{tab}/// </summary>");
                        sb.AppendLine($"{tab}{tab}" + $"public I{tableName}Repository" + $" {tableName}Repository " + $"=> IocManager.Instance.Resolve<I{tableName}Repository>();");
                    }

                    sb.AppendLine($"{tab}" + "}");
                    sb.AppendLine("}");
                    var str = sb.ToString();
                    File.WriteAllText(Path.Combine(folder, $"RepositoryService.generate.cs"), str, Encoding.UTF8);
                    _log.Write("********************************************************");
                    _log.Write("");
                }

                return true;
            }
            catch (Exception ex)
            {
                _log.Write(ex.Message);
                _log.Write(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// 如 t_user_follow => UserFollow
        /// </summary>
        /// <param name="table_name"></param>
        /// <returns></returns>
        private string TableNameToClassName(string table_name)
        {
            // .RemovePreFix("t")
            var names = table_name.Split('_', StringSplitOptions.RemoveEmptyEntries);
            List<string> namess = new List<string>();
            foreach (var name in names)
            {
                namess.Add(name.ToPascalCase());
            }
            var tableName = string.Join("", namess);
            return tableName;
        }

        /// <summary>
        /// 如 t_user_follow => T_USER_FOLLOW
        /// </summary>
        /// <param name="table_name"></param>
        /// <returns></returns>
        private string TableNameToEntityName(string table_name)
        {
            return table_name;
        }

        /// <summary>
        /// 生成文件头
        /// </summary>
        /// <returns></returns>
        private StringBuilder GenerateFileHeader()
        {
            var sb = new StringBuilder();
            sb.AppendLine("//---------------------------------------------------------------------------------------------------");
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine($"//    这段代码是由 {__namespace}.EntityGenerate 项目生成的，代码不需要任何修改就可以使用。");
            //sb.AppendLine($"//    生成时间：{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");
            sb.AppendLine("//    不要在生成文件中做任何修改，会被覆盖。");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine("//---------------------------------------------------------------------------------------------------");
            return sb;
        }
        /// <summary>
        /// 数据类型，int,varchar,datetime,bigint,tinyint
        /// </summary>
        /// <param name="dbDataType"></param>
        /// <param name="nullable"></param>
        /// <returns></returns>
        public string GetDataType(string dbDataType, string is_nullable)
        {
            var dt = dbDataType.ToLower();
            if (dt == "varchar" || dt == "nvarchar" || dt == "longtext" || dt == "ntext" || dt == "text"
                || dt == "uniqueidentifier" || dt == "char" || dt == "nchar"
                || dt == "geography")
            {
                return "string";
            }
            else if (dt == "date" || dt == "datetime" || dt == "datetime2" || dt == "smalldatetime")
            {
                if (is_nullable == "YES")
                {
                    return "System.DateTime?";
                }
                else
                {
                    return "System.DateTime";
                }
            }
            else if (dt == "int" || dt == "tinyint" || dt == "bit" || dt == "smallint")
            {
                if (is_nullable == "YES")
                {
                    return "int?";
                }
                else
                {
                    return "int";
                }
            }
            else if (dt == "bigint")
            {
                if (is_nullable == "YES")
                {
                    return "long?";
                }
                else
                {
                    return "long";
                }
            }
            else if (dt == "decimal" || dt == "money" || dt == "numeric" || dt == "smallmoney")
            {
                if (is_nullable == "YES")
                {
                    return "decimal?";
                }
                else
                {
                    return "decimal";
                }
            }
            else if (dt == "float" || dt == "real")
            {
                if (is_nullable == "YES")
                {
                    return "double?";
                }
                else
                {
                    return "double";
                }
            }
            else if (dt == "binary" || dt == "varbinary" || dt == "image")
            {
                return "byte[]";
            }
            else if (dt == "Variant")
            {
                return "object";
            }
            else if (dt == "datetimeoffset")
            {
                if (is_nullable == "YES")
                {
                    return "DateTimeOffset?";
                }
                else
                {
                    return "DateTimeOffset";
                }
            }
            // else if (dt == "uniqueidentifier")
            // {
            //     if (is_nullable == "YES")
            //     {
            //         return "System.Guid?";
            //     }
            //     else
            //     {
            //         return "System.Guid";
            //     }
            // }
            throw new Exception($"dataType 类型不支持：{dbDataType}");
        }
    }

    public readonly record struct TableNameDto(string table_name, string table_comment);
    // public class TableNameDto
    // {
    //     /// <summary>
    //     /// 表名
    //     /// </summary>
    //     public string table_name { get; set; }
    //     /// <summary>
    //     /// 注释
    //     /// </summary>
    //     public string table_comment { get; set; }
    // }
    public readonly record struct ColumnNameDto(
        string table_name,
        string column_name,
        string column_default,
        string is_nullable,
        string data_type,
        string column_comment,
        string character_maximum_length
    );
    // public class ColumnNameDto
    // {
    //     /// <summary>
    //     /// 列名
    //     /// </summary>
    //     public string column_name { get; set; }
    //     /// <summary>
    //     /// 默认值
    //     /// </summary>
    //     public string column_default { get; set; }
    //     /// <summary>
    //     /// 是否为空，YES/NO
    //     /// </summary>
    //     public string is_nullable { get; set; }
    //     /// <summary>
    //     /// 数据类型，int,varchar,datetime,bigint,tinyint
    //     /// </summary>
    //     public string data_type { get; set; }
    //     /// <summary>
    //     /// 注释
    //     /// </summary> 
    //     public string column_comment { get; set; }
    //     /// <summary>
    //     /// 字符串最大长度
    //     /// </summary>
    //     public string character_maximum_length { get; set; }
    // }
}
