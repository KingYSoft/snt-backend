-- 1. 合单主表：未取消过滤 + jk_pk 倒序分页
CREATE NONCLUSTERED INDEX IX_JobConsol_Tbl_Active_Pk
ON dbo.JobConsol
(
    jk_iscancelled,
    jk_pk DESC
);

-- 2. 合单关联运单：按合单查询运单
CREATE NONCLUSTERED INDEX IX_JobConShipLink_Jk_Js
ON dbo.JobConShipLink
(
    jn_jk,
    jn_js
);

-- 3. 合单关联运单：按运单反查合单
CREATE NONCLUSTERED INDEX IX_JobConShipLink_Js_Jk
ON dbo.JobConShipLink
(
    jn_js,
    jn_jk
);


-- 4. 合单运输信息：支持 OUTER APPLY TOP 1
CREATE NONCLUSTERED INDEX IX_JobConsolTransport_Tbl
ON dbo.JobConsolTransport
(
    jw_parentguid,
    jw_parenttype,
    jw_isvalid,
    jw_transporttype,
    jw_legorder,
    jw_pk
)
INCLUDE
(
    jw_etd,
    jw_eta,
    jw_vessel,
    jw_voyageflight,
    jw_oa_carrieraddress
);

-- 5. 集装箱：按合单查询有效状态及数量
CREATE NONCLUSTERED INDEX IX_JobContainer_Jk_Valid
ON dbo.JobContainer
(
    jc_jk,
    jc_isvalid
)
INCLUDE
(
    jc_rc,
    jc_containercount
);


-- 6. JobHeader：按父级类型和父级 ID 查找
CREATE NONCLUSTERED INDEX IX_JobHeader_ParentTable_ParentId
ON dbo.JobHeader
(
    jh_parenttablecode,
    jh_parentid,
    jh_pk
)
INCLUDE
(
    jh_gc,
    jh_gs_nkrepsales,
    jh_gs_nkrepops
);

-- 7. 自定义字段：客服信息查询
CREATE NONCLUSTERED INDEX IX_GenCustomAddOnValue_Parent_Name
ON dbo.GenCustomAddOnValue
(
    Xv_ParentID,
    XV_Name,
    XV_PK
)
INCLUDE
(
    XV_Data
);