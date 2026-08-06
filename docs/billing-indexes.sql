/*
    BillingCore.cs SQL Server indexes

    Covers billing queries for shipment (JobShipment) and consolidation
    (JobConsol), including the indexes previously recorded in snt索引sql.rtf.

    Select the target database before executing this script. The script does
    not contain a USE statement so that a production database is not selected
    accidentally.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO


/* =========================================================
   Indexes previously recorded in snt索引sql.rtf
   ========================================================= */

/* Find JobHeader rows belonging to a shipment or consolidation. */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_JobHeader_QueryChargeLine_Parent'
      AND object_id = OBJECT_ID(N'dbo.JobHeader')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_JobHeader_QueryChargeLine_Parent
    ON dbo.JobHeader (
        jh_parentid,
        jh_parenttablecode,
        jh_pk
    );
END;
GO


/* Query and order JobCharge rows belonging to a JobHeader. */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_JobCharge_QueryChargeLine_Job_Order'
      AND object_id = OBJECT_ID(N'dbo.JobCharge')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_JobCharge_QueryChargeLine_Job_Order
    ON dbo.JobCharge (
        jr_jh,
        jr_displaysequence,
        jr_pk
    )
    INCLUDE (
        jr_chargetype,
        jr_desc,
        jr_localsellamt,
        jr_ossellamt,
        jr_rx_nksellcurrency,
        jr_oh_sellaccount,
        jr_ossellexrate,
        jr_at_sellgstrate,
        jr_aw_sellwhtrate,
        jr_a9_sellvatclass,
        jr_al_arline,
        jr_localcostamt,
        jr_oscostamt,
        jr_rx_nkcostcurrency,
        jr_oh_costaccount,
        jr_oscostexrate,
        jr_at_costgstrate,
        jr_aw_costwhtrate,
        jr_a9_costvatclass,
        jr_al_apline,
        jr_productquantity,
        jr_isvalid
    );
END;
GO


/* =========================================================
   Additional BillingCore indexes
   ========================================================= */

/* Find JobCharge rows linked to AR transaction lines. */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_JobCharge_ArLine'
      AND object_id = OBJECT_ID(N'dbo.JobCharge')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_JobCharge_ArLine
    ON dbo.JobCharge (jr_al_arline)
    WHERE jr_al_arline IS NOT NULL;
END;
GO


/* Find JobCharge rows linked to AP transaction lines. */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_JobCharge_ApLine'
      AND object_id = OBJECT_ID(N'dbo.JobCharge')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_JobCharge_ApLine
    ON dbo.JobCharge (jr_al_apline)
    WHERE jr_al_apline IS NOT NULL;
END;
GO


/*
    Query transaction lines by invoice header for posting, draft editing,
    totals recalculation, deletion and credit-note creation.
*/
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_AccTransactionLines_Header_Sequence'
      AND object_id = OBJECT_ID(N'dbo.AccTransactionLines')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_AccTransactionLines_Header_Sequence
    ON dbo.AccTransactionLines (
        al_ah,
        al_sequence
    )
    INCLUDE (
        al_pk,
        al_jh,
        al_lineamount,
        al_osamount
    );
END;
GO


/* Resolve an invoice through al_jh when AccTransactionHeader.ah_jh is NULL. */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_AccTransactionLines_JobHeader'
      AND object_id = OBJECT_ID(N'dbo.AccTransactionLines')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_AccTransactionLines_JobHeader
    ON dbo.AccTransactionLines (
        al_jh,
        al_ah
    )
    WHERE al_jh IS NOT NULL;
END;
GO


/* Filter and page invoices belonging to a JobHeader. */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_AccTransactionHeader_JobHeader_Billing'
      AND object_id = OBJECT_ID(N'dbo.AccTransactionHeader')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_AccTransactionHeader_JobHeader_Billing
    ON dbo.AccTransactionHeader (
        ah_jh,
        ah_iscancelled,
        ah_ledger,
        ah_transactiontype,
        ah_invoicedate,
        ah_pk
    )
    INCLUDE (
        ah_transactionnum,
        ah_postdate
    );
END;
GO


/* Find an active posted invoice by transaction number before voiding it. */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_AccTransactionHeader_TransactionNum_Posted'
      AND object_id = OBJECT_ID(N'dbo.AccTransactionHeader')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_AccTransactionHeader_TransactionNum_Posted
    ON dbo.AccTransactionHeader (ah_transactionnum)
    INCLUDE (
        ah_pk,
        ah_ledger,
        ah_postdate,
        ah_outstandingamount,
        ah_invoiceamount,
        ah_jh
    )
    WHERE ah_iscancelled = 0
      AND ah_postdate IS NOT NULL;
END;
GO


/* Find shipments linked to a consolidation when choosing a JobHeader template. */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_JobConShipLink_Consol_Shipment'
      AND object_id = OBJECT_ID(N'dbo.JobConShipLink')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_JobConShipLink_Consol_Shipment
    ON dbo.JobConShipLink (
        jn_jk,
        jn_js
    );
END;
GO
