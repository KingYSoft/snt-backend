using SntBackend.Application.Company.Dto;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SntBackend.Application.Company
{
    public interface ICompanyApplication : ISntBackendApplicationBase
    {
        /// <summary>
        /// 按外币列表 + 发票日期查询卖出汇率。
        /// exrate_from = 传入外币；exrate_to = 公司本位币（或 InvoiceCurrency）；同币种返回 1。
        /// </summary>
        Task<List<CompanyQueryRateOutput>> Get(GetCompanyInput input);
    }
}
