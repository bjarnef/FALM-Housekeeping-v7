﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using FALM.Housekeeping.Helpers;
using FALM.Housekeeping.Models;
using FALM.Housekeeping.Services;
using Umbraco.Core.Logging;
using Umbraco.Web.Mvc;
using Umbraco.Web.WebApi;

namespace FALM.Housekeeping.Controllers
{
    /// <summary>
    /// PluginController("FALMHousekeeping")
    /// HkLogsApiController
    /// </summary>
    [PluginController("FALMHousekeeping")]
    public class HkLogsApiController : UmbracoApiController
    {
        /// <summary></summary>
        protected HKDBLogsModel DbLogsModel = new HKDBLogsModel();
        /// <summary></summary>
        protected List<DBLogModel> ListDbLogs = new List<DBLogModel>();
        /// <summary></summary>
        protected TraceLogsModel TraceLogsModel = new TraceLogsModel();
        /// <summary></summary>
        protected List<TraceLogDataModel> ListTraceLogs = new List<TraceLogDataModel>();
        /// <summary></summary>
        protected const string CombinedLogEntryPattern = @"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3}\s(\[(?<PROCESS1>.+)\]|\s) (?<LEVEL>\w+) {1,5}(?<LOGGER>.+?) -(\s\[(?<PROCESS2>[A-Z]\d{1,6}/[A-Z]\d{1,6}/[A-Z]\d{1,6}|Thread \d.?)\]\s|\s)(?<MESSAGE>.+)";
        /// <summary></summary>
        protected readonly Regex LogEntryRegex = new Regex(CombinedLogEntryPattern, RegexOptions.Singleline | RegexOptions.Compiled);
        /// <summary></summary>
        protected const string ThreadProcessPattern = @"T(?<THREAD>\d+)|D(?<DOMAIN>\d+)|P(?<PROCESS>\d+)|Thread (?<THREADOLD>\d+)";
        /// <summary></summary>
        protected static readonly Regex ThreadProcessRegex = new Regex(ThreadProcessPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Show all DB logs
        /// </summary>
        /// <returns>DBLogsModel</returns>
        [HttpGet]
        public HKDBLogsModel GetDbLogs()
        {
            ListDbLogs = new List<DBLogModel>();

            try
            {
                var sqlLog = "SELECT umbracoLog.id AS logId, umbracoLog.userId AS UserId, umbracoUser.userName AS UserName, umbracoUser.userLogin AS UserLogin, umbracoLog.NodeId AS NodeId, umbracoNode.text AS NodeName, umbracoLog.DateStamp AS LogDate, umbracoLog.logHeader AS LogHeader, umbracoLog.logComment AS LogComment ";
                sqlLog += "FROM umbracoLog INNER JOIN umbracoUser ON umbracoLog.userId = umbracoUser.id LEFT OUTER JOIN umbracoNode ON umbracoLog.NodeId = umbracoNode.id ";
                sqlLog += "ORDER BY umbracoLog.DateStamp DESC";

                using (var db = HkDbHelper.ResolveDatabase())
                {
                    ListDbLogs = db.Fetch<DBLogModel>(sqlLog);
                    DbLogsModel.ListDBLogs = ListDbLogs;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error<Exception>(ex.Message, ex);
                return DbLogsModel;
            }

            return DbLogsModel;
        }

        /// <summary>
        /// Delete filtered DB logs
        /// </summary>
        /// <param name="logsToDelete"></param>
        /// <returns>bool</returns>
        [HttpPost]
        public bool PostDeleteDbLogs(List<DBLogModel> logsToDelete)
        {
            try
            {
                using (var db = HkDbHelper.ResolveDatabase())
                {
                    var sqlDeleteLog = "DELETE FROM umbracoLog WHERE umbracoLog.id in (";

                    var iCount = 1;

                    foreach (var logItem in logsToDelete)
                    {
                        sqlDeleteLog += logItem.LogId.ToString();
                        sqlDeleteLog += iCount < logsToDelete.Count ? ", " : string.Empty;

                        iCount++;
                    }

                    sqlDeleteLog += ");";

                    db.Execute(sqlDeleteLog);
                }

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error<Exception>(ex.Message, ex);
                return false;
            }
        }

        /// <summary>
        /// Delete DB logs older than 6 months
        /// </summary>
        /// <returns>bool</returns>
        [HttpPost]
        public bool PostDeleteDbLogsBeforeMonths()
        {
            try
            {
                using (var db = HkDbHelper.ResolveDatabase())
                {
                    const string sqlDeleteLog = "DELETE FROM umbracoLog WHERE Datestamp < DATEADD(MONTH, -6, GETDATE())";

                    db.Execute(sqlDeleteLog);
                }

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error<Exception>(ex.Message, ex);
                return false;
            }
        }

        /// <summary>
        /// Show selected Trace Log
        /// </summary>
        /// <param name="filename"></param>
        /// <returns>TraceLogsModel</returns>
        [HttpGet]
        public TraceLogsModel GetTraceLogs(string filename)
        {
            try
            {
                ListTraceLogs = new List<TraceLogDataModel>();

                var currentTraceLogFile = Path.Combine(HkLogsService.GetBaseTraceLogPath(), filename);

                if (File.Exists(currentTraceLogFile))
                {
                    var logFile = File.ReadAllLines(currentTraceLogFile);
                    var logList = new List<string>(logFile);

                    foreach (var logEntry in logList)
                    {
                        var match = LogEntryRegex.Match(logEntry);

                        TraceLogDataModel traceLogItem;
                        if (match.Success)
                        {
                            // Get Log Entry Date Format yyyy-MM-dd HH:mm:ss (first 19 chars)
                            var logEntryData = logEntry.Trim().Substring(0, 19);

                            var date = DateTime.Parse(logEntryData);

                            var threadProcess = match.Groups["PROCESS2"].Value;

                            if (string.IsNullOrEmpty(threadProcess))
                            {
                                threadProcess = match.Groups["PROCESS1"].Value;
                            }

                            string threadId = null;
                            string processId = null;
                            string domainId = null;

                            if (!string.IsNullOrEmpty(threadProcess))
                            {
                                var procMatches = ThreadProcessRegex.Matches(threadProcess);

                                foreach (Match procMatch in procMatches)
                                {
                                    if (!procMatch.Success) continue;
                                    var grp = procMatch.Groups["THREAD"];
                                    if (grp.Success)
                                    {
                                        threadId = grp.Value;
                                    }

                                    grp = procMatch.Groups["PROCESS"];
                                    if (grp.Success)
                                    {
                                        processId = grp.Value;
                                    }

                                    grp = procMatch.Groups["DOMAIN"];
                                    if (grp.Success)
                                    {
                                        domainId = grp.Value;
                                    }

                                    if (threadId != null) continue;
                                    grp = procMatch.Groups["THREADOLD"];
                                    if (grp.Success)
                                    {
                                        threadId = grp.Value;
                                    }
                                }
                            }

                            traceLogItem = new TraceLogDataModel
                            {
                                LogDate = date,
                                LogProcess = processId,
                                LogDomain = domainId,
                                LogThread = threadId,
                                LogLevel = match.Groups["LEVEL"].Value,
                                LogLogger = match.Groups["LOGGER"].Value,
                                LogMessage = match.Groups["MESSAGE"].Value
                            };

                            ListTraceLogs.Add(traceLogItem);
                        }
                        else
                        {
                            if (ListTraceLogs.Count > 0)
                            {
                                traceLogItem = new TraceLogDataModel
                                {
                                    LogMessage = string.Concat(logEntry)
                                };
                                ListTraceLogs.Add(traceLogItem);
                            }
                        }

                        ListTraceLogs.Sort((x, y) => y.LogDate.CompareTo(x.LogDate));
                        TraceLogsModel.ListTraceLogs = ListTraceLogs;
                    }
                }
                else
                {
                    throw new FileNotFoundException("The requested trace log file '" + Path.GetFileName(currentTraceLogFile) + "' could not be found", currentTraceLogFile);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error<Exception>(ex.Message, ex);
                return TraceLogsModel;
            }
            return TraceLogsModel;
        }
    }
}