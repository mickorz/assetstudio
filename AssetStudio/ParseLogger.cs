using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace AssetStudio
{
    public class ParseErrorEntry
    {
        public DateTime Timestamp { get; set; }
        public string FilePath { get; set; }
        public string OriginalPath { get; set; }
        public string SourceType { get; set; }
        public string ErrorPhase { get; set; }
        public string AssetName { get; set; }
        public string AssetType { get; set; }
        public long? PathID { get; set; }
        public string ExceptionType { get; set; }
        public string ExceptionMessage { get; set; }
        public string ExceptionStackTrace { get; set; }
    }

    public class ParseSession
    {
        public DateTime SessionStart { get; set; }
        public DateTime SessionEnd { get; set; }
        public string[] LoadedFiles { get; set; }
        public List<ParseErrorEntry> Errors { get; set; } = new List<ParseErrorEntry>();
    }

    public static class ParseLogger
    {
        private static ConcurrentBag<ParseErrorEntry> _errors = new ConcurrentBag<ParseErrorEntry>();
        private static string _logDirectory = "Logs";
        private static string _failedFilesDirectory;
        private static bool _enabled;
        private static DateTime _sessionStart;
        private static string[] _sessionFiles;
        private static int _sessionIdCounter;

        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public static string LogDirectory => _logDirectory;

        /// <summary>
        /// 开始新的解析会话
        /// </summary>
        public static void BeginSession(string[] files)
        {
            if (!_enabled) return;

            Interlocked.Increment(ref _sessionIdCounter);
            _errors = new ConcurrentBag<ParseErrorEntry>();
            _sessionStart = DateTime.Now;
            _sessionFiles = files;
        }

        /// <summary>
        /// 记录解析错误
        /// </summary>
        public static void LogError(
            string filePath,
            string errorPhase,
            Exception exception,
            string originalPath = null,
            string sourceType = null,
            string assetName = null,
            string assetType = null,
            long? pathID = null)
        {
            if (!_enabled) return;

            var entry = new ParseErrorEntry
            {
                Timestamp = DateTime.Now,
                FilePath = filePath,
                OriginalPath = originalPath,
                SourceType = sourceType,
                ErrorPhase = errorPhase,
                AssetName = assetName,
                AssetType = assetType,
                PathID = pathID,
                ExceptionType = exception?.GetType().FullName,
                ExceptionMessage = exception?.Message,
                ExceptionStackTrace = exception?.StackTrace
            };

            _errors.Add(entry);
        }

        /// <summary>
        /// 结束当前会话并保存日志
        /// </summary>
        public static int EndSession()
        {
            if (!_enabled) return 0;

            var errors = _errors.ToArray();
            if (errors.Length == 0) return 0;

            try
            {
                Directory.CreateDirectory(_logDirectory);

                var session = new ParseSession
                {
                    SessionStart = _sessionStart,
                    SessionEnd = DateTime.Now,
                    LoadedFiles = _sessionFiles,
                    Errors = errors.OrderBy(e => e.Timestamp).ToList()
                };

                var timestamp = _sessionStart.ToString("yyyyMMdd_HHmmss");
                var logFileName = $"parse_{timestamp}_{_sessionIdCounter}.json";
                var logPath = Path.Combine(_logDirectory, logFileName);

                var json = JsonConvert.SerializeObject(session, Formatting.Indented);
                File.WriteAllText(logPath, json);

                // 复制失败的源文件到 FailedFiles 子目录
                var failedDirName = $"parse_{timestamp}_{_sessionIdCounter}";
                _failedFilesDirectory = Path.Combine(_logDirectory, "FailedFiles", failedDirName);

                CopyFailedFiles(errors);

                Logger.Info($"解析日志已保存: {logPath} ({errors.Length} 个错误)");
                return errors.Length;
            }
            catch (Exception e)
            {
                Logger.Error("保存解析日志失败", e);
                return 0;
            }
        }

        private static void CopyFailedFiles(ParseErrorEntry[] errors)
        {
            // 收集需要复制的唯一文件路径
            var filesToCopy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in errors)
            {
                if (!string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath))
                {
                    filesToCopy.Add(entry.FilePath);
                }
                if (!string.IsNullOrEmpty(entry.OriginalPath) && File.Exists(entry.OriginalPath))
                {
                    filesToCopy.Add(entry.OriginalPath);
                }
            }

            if (filesToCopy.Count == 0) return;

            Directory.CreateDirectory(_failedFilesDirectory);

            foreach (var filePath in filesToCopy)
            {
                try
                {
                    var fileName = Path.GetFileName(filePath);
                    var destPath = Path.Combine(_failedFilesDirectory, fileName);

                    // 处理同名文件
                    if (File.Exists(destPath))
                    {
                        var dir = Path.GetDirectoryName(destPath);
                        var nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                        var ext = Path.GetExtension(fileName);
                        destPath = Path.Combine(dir, $"{nameNoExt}_{Guid.NewGuid().ToString("N").Substring(0, 8)}{ext}");
                    }

                    File.Copy(filePath, destPath);
                }
                catch (Exception e)
                {
                    Logger.Verbose($"复制失败文件 {filePath} 时出错: {e.Message}");
                }
            }
        }
    }
}
