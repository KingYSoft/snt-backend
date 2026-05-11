# Billing 接口移植设计（first-cargo-backend → snt-backend）

## 1. 任务范围

把 first-cargo-backend `IBillingApplication` 中 4 个接口移植到 snt-backend，服务于 `feature/shp-page` 分支的 shipment 详情页。

| 源接口 | 用途 |
|---|---|
| `QueryMatchingchargeLine`（项目里叫 QueryChargeLine） | 按 shpPk + chargeType 分页查询费用明细 |
| `QueryDraftPage` | 草稿箱分页（待开票费用） |
| `GetBillingSummary` | 账单汇总（毛利率、AR、AP、利润） |
| `QueryChargesByInvoiceNo` | 按发票号反查关联费用明细 |

**重要前提**：snt-backend 没有 `tmp / draft` 概念（即没有 `TTL_TEMP_LINES` 这种"待过账暂存"表）。

---

## 2. 已经验证的事实（来自 SQL 探测）

### 2.1 表/字段映射

| 源系统（first-cargo） | snt-backend |
|---|---|
| `JCH_JOB_CHARGE`（预录费用） | `JobCharge`（前缀 `jr_*`） |
| `TTL_TEMP_LINES`（草稿） | **无**（snt 没有 draft 表） |
| `TTH_TRANSACTION_HEADER`（已过账发票头） | `AccTransactionHeader`（前缀 `ah_*`） |
| `TTL_TRANSACTION_LINES`（已过账发票行） | `AccTransactionLines`（前缀 `al_*`） |
| `JOB_JOB`（作业头） | `JobHeader`（前缀 `jh_*`） |
| `SHP_SHIPMENT`（运单） | `JobShipment`（前缀 `js_*`） |
| `SYS_BRANCH`（分公司） | `GlbBranch`（前缀 `gb_*`） |
| `ORG_HEADER`（组织） | `OrgHeader`（前缀 `oh_*`） |

### 2.2 关键关联（已验证）

```
JobShipment.js_pk
   ↑
   └── JobHeader.jh_parentid  (WHERE jh_parenttablecode = 'JS')   ← 注意是 'JS' 不是 'SHP'
                ↑
                ├── JobCharge.jr_jh                   （预录费用，BTH 模型）
                ├── AccTransactionHeader.ah_jh        （已过账发票头）
                └── AccTransactionLines （通过 al_ah → ah_pk 间接关联）

JobCharge.jr_al_arline → AccTransactionLines.al_pk    （AR 侧已过账后的行）
JobCharge.jr_al_apline → AccTransactionLines.al_pk    （AP 侧已过账后的行）
AccTransactionLines.al_ah → AccTransactionHeader.ah_pk
```

`jh_parenttablecode` 实际取值分布：
- `JS` 45,206 行（直挂 JobShipment）
- `TH` 240 行（少量，暂时忽略）

不需要走 `JobConShipLink` / `JobConsol` 中转。

### 2.3 BTH 模型（最关键）

**`jr_linetype` 100% 都是 `BTH`**（556,332 / 556,332）。

**一行 JobCharge 同时包含 AR（销售）和 AP（成本）两侧**，每侧有独立的金额、币种、组织、发票链接：

| 维度 | AR / 销售侧 | AP / 成本侧 |
|---|---|---|
| 本位币金额 | `jr_localsellamt` | `jr_localcostamt` |
| 原币金额 | `jr_ossellamt` | `jr_oscostamt` |
| 币种 | `jr_rx_nksellcurrency` | `jr_rx_nkcostcurrency` |
| 汇率 | `jr_ossellexrate` | `jr_oscostexrate` |
| 客户/供应商 | `jr_oh_sellaccount` | `jr_oh_costaccount` |
| 已过账发票行链接 | `jr_al_arline` | `jr_al_apline` |
| 过账状态字段 | `jr_arlinepostingstatus` | `jr_aplinepostingstatus` |

样本（来自实际数据）：
```
jr_pk           linetype  AR amt   AR ccy  AR oh        AR alline       AP amt    AP ccy  AP oh        AP alline
19CD3C04...     BTH       450      CNY     4AA7...      A7207E7E...     450       CNY     0EC6...      1CD6CA6A...   ← 两侧都有
9AF5D9B7...     BTH       0        USD     (空)         (空)             177.76    USD     0F8C9190...  A33802F1...   ← 只有 AP 侧
```

所以前端"按 AR 看 charges"实际是**取这一侧的视角投影**，不是"过滤 jr_linetype = AR"。

### 2.4 Draft 判断信号

`jr_arlinepostingstatus` / `jr_aplinepostingstatus` 实际数据里多数为空，**不可靠**。

可靠信号：
```
AR 侧 Draft = (jr.jr_al_arline IS NULL)
AP 侧 Draft = (jr.jr_al_apline IS NULL)
```

数据分布证明（仅 BTH 行）：
| AR alline | AP alline | 行数 |
|---|---|---|
| NOT NULL | NOT NULL | 321,917 |
| NULL | NOT NULL | 145,254 |
| NOT NULL | NULL | 86,281 |
| NULL | NULL | 2,880 |

### 2.5 AccTransactionHeader 类型

| ah_ledger | ah_transactiontype | 总数 | 含 ah_jh | 用途 |
|---|---|---|---|---|
| AR | INV | 73,865 | 71,403 | **AR 发票** ✓ |
| AP | INV | 48,072 | 33,994 | **AP 发票** ✓ |
| AP | PAY | 34,595 | 0 | 付款 |
| AR | REC | 30,320 | 0 | 收款 |
| CB | RCB | 28,215 | 0 | 现金簿 |
| AR | EXX | 5,843 | 0 | 汇率调整 |
| AP/AR | CTR | 各 3,758 | 0 | 冲销 |
| AR | CRD | 921 | 918 | 信用单 |
| ...其他 | | | | |

**没有 BILL 这个值**，所以代码里的 `IN ('INV', 'BILL')` 应改成 `= 'INV'`（如果还需要包含 CRD / CTR 视情况而定）。

### 2.6 AccTransactionLines.al_linetype

实际取值是 **WIP / ACR / CST / REV**（会计科目维度），不是 AR/AP。AR/AP 只能从 `AccTransactionHeader.ah_ledger` 推导。

---

## 3. 当前已写代码的问题

文件：
- `src/SntBackend.Application/Billing/IBillingApplication.cs`
- `src/SntBackend.Application/Billing/BillingApplication.cs`
- `src/SntBackend.Application/Billing/Dto/Billing*.cs`、`QueryChargesByInvoiceOutput.cs`
- `src/SntBackend.Web.Host/Controllers/BillingController.cs`

| 问题 | 当前实现 | 应改成 |
|---|---|---|
| jh 过滤值错误 | `jh_parenttablecode = 'SHP'` | `'JS'` |
| 发票类型错误 | `ah_transactiontype IN ('INV','BILL')` | `= 'INV'` |
| BTH 模型未处理 | `WHERE jr.jr_linetype = @chargeType` 直接按 AR/AP 过滤行 | 按所选侧投影 + 该侧有效性过滤 |
| Draft 字段缺失 | 无 | 加 `Draft` 字段，按所选侧的 al_*line 是否 NULL 推导 |
| GetBillingSummary 数据源 | 用 `AccTransactionHeader.ah_invoiceamount` 汇总 | 用 `JobCharge.jr_localsellamt/jr_localcostamt` 汇总（对应源系统逻辑） |

---

## 4. 修改方案

### 4.1 公共关联子句（所有 SQL 复用）

```sql
INNER JOIN JobHeader jh ON jh.jh_pk = jr.jr_jh
WHERE jh.jh_parentid = @shpPk
    AND jh.jh_parenttablecode = 'JS'
    AND jh.jh_isvalid = 1
```

### 4.2 QueryChargeLine 重写

**入参**：`{ shpPk, chargeType: 'AR'|'AP', SkipCount, MaxResultCount, Sorting }`

**SQL（chargeType=AR 时）**：
```sql
SELECT jr.jr_pk,
       jr.jr_jh,
       jr.jr_chargetype,
       jr.jr_desc,
       jr.jr_localsellamt    AS amount,
       jr.jr_ossellamt       AS os_amount,
       jr.jr_rx_nksellcurrency AS currency,
       jr.jr_oh_sellaccount  AS party_oh,
       jr.jr_ossellexrate    AS exchange_rate,
       jr.jr_at_sellgstrate  AS gst_rate,
       jr.jr_aw_sellwhtrate  AS wht_rate,
       jr.jr_a9_sellvatclass AS vat_class,
       jr.jr_al_arline       AS line_pk,
       arInv.ah_pk           AS invoice_pk,
       arInv.ah_transactionnum AS invoice_no,
       arInv.ah_invoicedate  AS invoice_date,
       CASE WHEN jr.jr_al_arline IS NULL THEN 'Y' ELSE 'N' END AS Draft
FROM JobCharge jr
INNER JOIN JobHeader jh ON jh.jh_pk = jr.jr_jh
LEFT JOIN AccTransactionLines arLine ON arLine.al_pk = jr.jr_al_arline
LEFT JOIN AccTransactionHeader arInv ON arInv.ah_pk = arLine.al_ah AND arInv.ah_iscancelled = 0
WHERE jh.jh_parentid = @shpPk
    AND jh.jh_parenttablecode = 'JS'
    AND jh.jh_isvalid = 1
    AND ( jr.jr_localsellamt <> 0
       OR jr.jr_al_arline IS NOT NULL
       OR jr.jr_oh_sellaccount IS NOT NULL )    -- 过滤掉 AR 侧根本没用的行
ORDER BY ...
OFFSET ... ROWS FETCH NEXT ... ROWS ONLY;
```

chargeType=AP 时：所有 `sell` 字段换成 `cost`，`arline` 换成 `apline`，过滤条件用 `jr_localcostamt / jr_al_apline / jr_oh_costaccount`。

### 4.3 QueryDraftPage 重写（已确认：直接查发票头，不分组）

**入参**：`{ shpPk, chargeType: 'AR'|'AP', SkipCount, MaxResultCount, Sorting }`

**SQL**：
```sql
SELECT ah.*
FROM AccTransactionHeader ah
INNER JOIN JobHeader jh ON jh.jh_pk = ah.ah_jh
WHERE jh.jh_parentid = @shpPk
    AND jh.jh_parenttablecode = 'JS'
    AND jh.jh_isvalid = 1
    AND ah.ah_iscancelled = 0
    AND ah.ah_transactiontype = 'INV'
    AND ah.ah_ledger = @chargeType        -- chargeType 必传 AR/AP
ORDER BY ah.ah_invoicedate DESC, ah.ah_pk DESC
OFFSET ... ROWS FETCH NEXT ... ROWS ONLY;
```

**输出**：`AccTransactionHeaderDtoOutput` 列表（不再加 Draft 字段，因为 snt 全部是已过账状态）。

DTO 简化为：
```csharp
public class BillingDraftPageOutput
{
    public int TotalCount { get; set; }
    public List<AccTransactionHeaderDtoOutput> Items { get; set; } = new();
}
```
不再继承包装类，`BillingDraftPageItem` 类删除。

### 4.4 GetBillingSummary 重写

```sql
SELECT
    ISNULL(SUM(jr.jr_localsellamt), 0) AS ar,
    ISNULL(SUM(jr.jr_localcostamt), 0) AS ap
FROM JobCharge jr
INNER JOIN JobHeader jh ON jh.jh_pk = jr.jr_jh
WHERE jh.jh_parentid = @shpPk
    AND jh.jh_parenttablecode = 'JS'
    AND jh.jh_isvalid = 1;
```

`profits = ar - ap`，`grossProfitMargin = ar > 0 ? profits/ar*100 : 0`。

`home_currency` 暂时返回空字符串（snt `AbpSession` 没有该信息；后续接 GlbCompany 配置或运维指定）。

### 4.5 QueryChargesByInvoiceNo 重写

不变，只改两处：
- `ah_transactiontype IN ('INV','BILL')` → `= 'INV'`（其实不传也行，发票号本身唯一）
- 额外按 `jr_al_arline = al_pk` 或 `jr_al_apline = al_pk` 反查 JobCharge，给前端"这张发票包含哪些预录费用"

### 4.6 输出 DTO 变化

```csharp
public class BillingChargeLineItem
{
    public string jr_pk { get; set; }
    public string jr_jh { get; set; }
    public string jr_chargetype { get; set; }       // 业务费用代码（FRT/THC...保留）
    public string jr_desc { get; set; }
    public decimal? amount { get; set; }            // 投影：localsellamt 或 localcostamt
    public decimal? os_amount { get; set; }
    public string currency { get; set; }
    public string party_oh { get; set; }
    public decimal? exchange_rate { get; set; }
    public string gst_rate { get; set; }
    public string wht_rate { get; set; }
    public string vat_class { get; set; }
    public string line_pk { get; set; }             // jr_al_arline 或 jr_al_apline
    public string invoice_pk { get; set; }          // ah_pk
    public string invoice_no { get; set; }          // ah_transactionnum
    public DateTime? invoice_date { get; set; }
    public string Draft { get; set; }               // 'Y' / 'N'
}
```

去掉之前那个继承 `JobChargeDtoOutput` 的设计，改用扁平投影（前端容易消费）。

---

## 5. 决策记录

| 项 | 决策 |
|---|---|
| QueryDraftPage 形态 | **直接展示 AccTransactionHeader，不分组**，`Items` 用 `AccTransactionHeaderDtoOutput` |
| JobHeader 过滤字段 | **`jh_isvalid = 1`** |
| branch 过滤 | **暂不过滤** |
| 是否含 CRD/CTR 等非 INV 类型 | 待定，**默认仅 `INV`**；如果业务要把信用单也算 AR/AP 再加 |

---

## 6. 落地步骤

1. 用户确认本文档 + 开放问题
2. 删除/重写 `BillingApplication.cs` 中 4 个方法的实现
3. 修改 DTO（`BillingChargeLineOutput`、`BillingDraftPageOutput`、`QueryChargesByInvoiceOutput`、`BillingSummaryDto`）
4. 接口签名 + Controller 路由保持不变
5. `dotnet build` 通过
6. 提供给前端联调用的样例 curl
