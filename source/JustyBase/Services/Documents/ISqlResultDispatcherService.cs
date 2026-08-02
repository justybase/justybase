using System;
using System.Data.Common;
using JustyBase.Common.Models;
using JustyBase.Common.Services;
using JustyBase.Helpers.Shared;
using JustyBase.Models.Tools;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services.Documents;

public interface ISqlResultDispatcherService
{
    void DispatchGridResult(
        string instanceId, 
        ISqlResultManager resultManager, 
        IDatabaseService actualDatabaseService, 
        string? resTitle, 
        string? query, 
        LogMessage? currentLogMessage, 
        bool tabsWithRows, 
        int actualQueryNum, 
        ref int globalAbortUBound, 
        DbDataReader rdr, 
        DbCommand cmd, 
        string? shortQuery);

    void DispatchErrorResult(
        string instanceId,
        ISqlResultManager resultManager,
        string? localTitle,
        LogMessage? currentLogMessage,
        int actualQueryNum,
        ref int globalAbortUBound,
        IDatabaseService actualDatabaseService,
        int currentSqlNumber,
        string? sql,
        DbCommand cmd,
        Exception exx1);

    void DispatchWarningResult(
        string instanceId,
        ISqlResultManager resultManager,
        string? localTitle,
        int actualQueryNum,
        ref int globalAbortUBound,
        IDatabaseService? actualDatabaseService);

    void DispatchRuntimeErrorResult(
        string instanceId,
        ISqlResultManager resultManager,
        string? localTitle,
        int actualQueryNum,
        ref int globalAbortUBound,
        Exception exception);

    void DispatchRecordsAffected(LogMessage? currentLogMessage, DbDataReader rdr);

    void ClosePreviousResults(string instanceId, ISqlResultManager resultManager);
}

public class SqlResultDispatcherService : ISqlResultDispatcherService
{
    public void DispatchGridResult(
        string instanceId,
        ISqlResultManager resultManager,
        IDatabaseService actualDatabaseService,
        string? resTitle,
        string? query,
        LogMessage? currentLogMessage,
        bool tabsWithRows,
        int actualQueryNum,
        ref int globalAbortUBound,
        DbDataReader rdr,
        DbCommand cmd,
        string? shortQuery)
    {
        resTitle = SqlDocumentViewModelHelper.ParseResultTitle(shortQuery, resTitle ?? string.Empty);

        if (rdr.HasRows)
        {
            currentLogMessage?.AddInnerMessageInUiThread($"loaded rows from  [{shortQuery} ...]", DateTime.Now);
        }

        if (rdr.HasRows || !tabsWithRows)
        {
            resultManager.AddNewResult(
                (actualDatabaseService, rdr, ""),
                instanceId,
                actualQueryNum,
                ref globalAbortUBound,
                query,
                cmd,
                resTitle);
        }
    }

    public void DispatchErrorResult(
        string instanceId,
        ISqlResultManager resultManager,
        string? localTitle,
        LogMessage? currentLogMessage,
        int actualQueryNum,
        ref int globalAbortUBound,
        IDatabaseService actualDatabaseService,
        int currentSqlNumber,
        string? sql,
        DbCommand cmd,
        Exception exx1)
    {
        int commandLength = Math.Min(cmd.CommandText.Length, 100);
        string shortQuery = cmd.CommandText[..commandLength].Trim().Replace("\n", " ").Replace("\r", " ");
        string resTitle = SqlDocumentViewModelHelper.ParseResultTitle(shortQuery, $"{localTitle}_{currentSqlNumber}");

        if (exx1.Message != "ERROR: Query was cancelled.")
        {
            resultManager.AddNewResult(
                (actualDatabaseService, null, exx1.Message),
                instanceId,
                actualQueryNum,
                ref globalAbortUBound,
                sql,
                null,
                resTitle);
        }

        currentLogMessage?.AddInnerMessageInUiThread($"⛔ {exx1.Message}", DateTime.Now);
        if (currentLogMessage is not null)
        {
            currentLogMessage.MessageType = LogMessageType.error;
        }
    }

    public void DispatchWarningResult(
        string instanceId,
        ISqlResultManager resultManager,
        string? localTitle,
        int actualQueryNum,
        ref int globalAbortUBound,
        IDatabaseService? actualDatabaseService)
    {
        resultManager.AddNewResult(
            (actualDatabaseService, null, "cannot establish connection"),
            instanceId,
            actualQueryNum,
            ref globalAbortUBound,
            null,
                null,
                localTitle);
    }

    public void DispatchRuntimeErrorResult(
        string instanceId,
        ISqlResultManager resultManager,
        string? localTitle,
        int actualQueryNum,
        ref int globalAbortUBound,
        Exception exception)
    {
        if (ShouldSuppressRuntimeErrorResult(exception))
        {
            return;
        }

        resultManager.AddNewResult(
            (null, null, exception.Message),
            instanceId,
            actualQueryNum,
            ref globalAbortUBound,
            null,
            null,
            localTitle);
    }

    public void DispatchRecordsAffected(LogMessage? currentLogMessage, DbDataReader rdr)
    {
        currentLogMessage?.AddInnerMessageInUiThread($"records affected {rdr.RecordsAffected:N0}", DateTime.Now);
    }

    public void ClosePreviousResults(string instanceId, ISqlResultManager resultManager)
    {
        resultManager.ClosePrevResults(instanceId);
    }

    private static bool ShouldSuppressRuntimeErrorResult(Exception exception)
    {
        return string.Equals(exception.Message, "Operation is not supported on this platform.", StringComparison.Ordinal)
            && string.Equals(exception.Source, "NZdotNETSlim", StringComparison.Ordinal);
    }
}
