using Dapper;
using SntBackend.Application.SystemSettings.Dto;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SntBackend.Application.SystemSettings
{
    internal static class SystemSettingsQueryHelper
    {
        public static int NormalizeSkip(int skipCount) => Math.Max(0, skipCount);

        public static int NormalizeLimit(int maxResultCount)
        {
            if (maxResultCount <= 0)
                return 20;

            return Math.Min(maxResultCount, 200);
        }

        public static string BuildTextFilter(
            string column,
            SystemFilterItem filter,
            DynamicParameters parameters,
            string parameterName)
        {
            if (filter == null || string.IsNullOrWhiteSpace(filter.val))
                return null;

            var op = string.IsNullOrWhiteSpace(filter.op) ? "Contain" : filter.op.Trim();
            var value = filter.val.Trim();
            switch (op)
            {
                case "Equal":
                    parameters.Add(parameterName, value);
                    return $"{column} = @{parameterName}";
                case "Not Equal":
                case "NotEqual":
                    parameters.Add(parameterName, value);
                    return $"{column} <> @{parameterName}";
                case "Contain":
                    parameters.Add(parameterName, $"%{value}%");
                    return $"{column} LIKE @{parameterName}";
                case "Not Contain":
                case "NotContain":
                    parameters.Add(parameterName, $"%{value}%");
                    return $"{column} NOT LIKE @{parameterName}";
                default:
                    throw new ArgumentException($"Unsupported filter operator: {op}");
            }
        }

        public static void AddTextSearch(
            string query,
            DynamicParameters parameters,
            string parameterName,
            params string[] columns)
        {
            if (string.IsNullOrWhiteSpace(query) || columns == null || columns.Length == 0)
                return;

            parameters.Add(parameterName, $"%{query.Trim()}%");
        }

        public static string JoinConditions(IEnumerable<string> conditions)
        {
            var items = conditions?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>();
            return items.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", items);
        }

        public static string NewPk() => Guid.NewGuid().ToString();
    }
}
