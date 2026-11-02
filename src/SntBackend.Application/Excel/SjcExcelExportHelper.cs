using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace SntBackend.Application.Excel
{
    public static class SjcExcelExportHelper
    {
        public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        public static IReadOnlyList<ExportColumnInput> ResolveColumns(
            IEnumerable<ExportColumnInput> requested,
            IReadOnlyList<ExportColumnInput> defaults)
        {
            var allowed = defaults
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.key))
                .ToDictionary(item => item.key, StringComparer.OrdinalIgnoreCase);
            var result = new List<ExportColumnInput>();

            foreach (var item in requested ?? Enumerable.Empty<ExportColumnInput>())
            {
                var key = item?.key?.Trim();
                if (string.IsNullOrWhiteSpace(key) || !allowed.TryGetValue(key, out var fallback))
                    continue;

                result.Add(new ExportColumnInput
                {
                    key = fallback.key,
                    title = string.IsNullOrWhiteSpace(item.title) ? fallback.title : item.title.Trim()
                });
            }

            return result.Count > 0 ? result : defaults;
        }

        public static byte[] Build(
            string sheetName,
            string title,
            string companyName,
            string period,
            IReadOnlyList<string> headers,
            IReadOnlyList<IReadOnlyList<object>> rows,
            ISet<int> numericColumns = null,
            IReadOnlyList<int> fixedWidths = null,
            string companyNameLabel = null,
            string reportPeriodLabel = null)
        {
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add(sheetName);
            var numericColumnSet = numericColumns ?? new HashSet<int>();

            worksheet.Cells[1, 1].Value = title ?? string.Empty;
            worksheet.Cells[2, 1].Value = string.IsNullOrWhiteSpace(companyNameLabel) ? "Company Name: " : companyNameLabel;
            worksheet.Cells[2, 2].Value = companyName ?? string.Empty;
            worksheet.Cells[3, 1].Value = string.IsNullOrWhiteSpace(reportPeriodLabel) ? "Report Generated Period: " : reportPeriodLabel;
            worksheet.Cells[3, 2].Value = period ?? string.Empty;

            for (var index = 0; index < headers.Count; index++)
            {
                worksheet.Cells[5, index + 1].Value = headers[index] ?? string.Empty;
            }

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                for (var columnIndex = 0; columnIndex < headers.Count; columnIndex++)
                {
                    var value = columnIndex < row.Count ? row[columnIndex] : null;
                    worksheet.Cells[rowIndex + 6, columnIndex + 1].Value = value switch
                    {
                        DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss"),
                        _ => value ?? string.Empty
                    };
                }
            }

            worksheet.View.FreezePanes(6, 1);
            worksheet.Row(1).Height = 30;
            worksheet.Row(2).Height = 18;
            worksheet.Row(3).Height = 18;
            worksheet.Row(4).Height = 18;
            worksheet.Row(5).Height = 34.5;

            worksheet.Cells[1, 1].Style.Font.Name = "Calibri";
            worksheet.Cells[1, 1].Style.Font.Size = 16;
            worksheet.Cells[1, 1].Style.Font.Bold = true;
            worksheet.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            worksheet.Cells[1, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Top;
            for (var rowIndex = 2; rowIndex <= 3; rowIndex++)
            {
                worksheet.Cells[rowIndex, 1].Style.Font.Name = "Calibri";
                worksheet.Cells[rowIndex, 1].Style.Font.Size = 11;
                worksheet.Cells[rowIndex, 1].Style.Font.Bold = true;
                worksheet.Cells[rowIndex, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
                worksheet.Cells[rowIndex, 2].Style.Font.Name = "Aptos";
                worksheet.Cells[rowIndex, 2].Style.Font.Size = 9;
                worksheet.Cells[rowIndex, 2].Style.Font.Bold = true;
                worksheet.Cells[rowIndex, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            }

            using (var header = worksheet.Cells[5, 1, 5, headers.Count])
            {
                header.Style.Font.Name = "Aptos";
                header.Style.Font.Size = 10;
                header.Style.Font.Bold = true;
                header.Style.Font.Color.SetColor(Color.White);
                header.Style.Fill.PatternType = ExcelFillStyle.Solid;
                header.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0, 102, 0));
                header.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                header.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                header.Style.WrapText = true;
            }

            for (var rowIndex = 6; rowIndex <= worksheet.Dimension.End.Row; rowIndex++)
            {
                worksheet.Row(rowIndex).Height = 18;
                worksheet.Row(rowIndex).Style.Font.Name = "Aptos";
                worksheet.Row(rowIndex).Style.Font.Size = 9;
                worksheet.Row(rowIndex).Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                worksheet.Row(rowIndex).Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            }

            for (var columnIndex = 0; columnIndex < headers.Count; columnIndex++)
            {
                var column = worksheet.Column(columnIndex + 1);
                column.Style.Numberformat.Format = numericColumnSet.Contains(columnIndex) ? "0.00" : "@";
                var width = 0;
                for (var rowIndex = 1; rowIndex <= worksheet.Dimension.End.Row; rowIndex++)
                {
                    var text = worksheet.Cells[rowIndex, columnIndex + 1].Text ?? string.Empty;
                    width = Math.Max(width, text.Split(new[] { '\r', '\n' }, StringSplitOptions.None)
                        .Select(line => line.Sum(ch => ch > 255 ? 2 : 1))
                        .DefaultIfEmpty(0)
                        .Max());
                }

                var configuredWidth = fixedWidths != null && columnIndex < fixedWidths.Count
                    ? fixedWidths[columnIndex]
                    : 0;
                if (columnIndex == 0)
                {
                    width = Math.Max(width, 24);
                }
                column.Width = configuredWidth > 0
                    ? Math.Min(58, Math.Max(10, configuredWidth))
                    : Math.Min(58, Math.Max(10, width + 2));
            }

            return package.GetAsByteArray();
        }
    }
}
