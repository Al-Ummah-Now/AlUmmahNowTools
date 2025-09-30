using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AlUmmahNow.VisualImporter.Services
{
    /// <summary>
    /// Windows Event Logger for tracking application events and crashes
    /// </summary>
    public class WindowsEventLogger
    {
        private const string EventSourceName = "AlUmmahNow.VisualImporter";
        private const string EventLogName = "Application";
        private static readonly EventLog _eventLog;
        
        static WindowsEventLogger()
        {
            try
            {
                // Create event source if it doesn't exist
                if (!EventLog.SourceExists(EventSourceName))
                {
                    EventLog.CreateEventSource(EventSourceName, EventLogName);
                }
                
                _eventLog = new EventLog(EventLogName, Environment.MachineName, EventSourceName);
            }
            catch (Exception ex)
            {
                // Fallback to debug output if event logging fails
                Debug.WriteLine($"Failed to initialize Windows Event Logger: {ex.Message}");
                _eventLog = null!;
            }
        }

        /// <summary>
        /// Log application startup
        /// </summary>
        public static void LogStartup()
        {
            LogEvent(EventLogEntryType.Information, 1001, 
                "AlUmmahNow Visual Importer started successfully",
                $"Application Version: 1.0.0.0\n" +
                $"Process ID: {Environment.ProcessId}\n" +
                $"Start Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $".NET Version: {Environment.Version}");
        }

        /// <summary>
        /// Log COM/WinRT marshaling errors
        /// </summary>
        public static void LogComMarshalingError(Exception ex, string operation)
        {
            LogEvent(EventLogEntryType.Error, 2001,
                $"COM/WinRT Marshaling Error in {operation}",
                $"Exception Type: {ex.GetType().Name}\n" +
                $"Message: {ex.Message}\n" +
                $"Stack Trace: {ex.StackTrace}\n" +
                $"Operation: {operation}\n" +
                $"Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
        }

        /// <summary>
        /// Log dispatcher queue failures
        /// </summary>
        public static void LogDispatcherFailure(string operation, Exception? ex = null)
        {
            var message = ex != null ? 
                $"Dispatcher queue operation failed: {ex.Message}" :
                $"Dispatcher queue not available for operation: {operation}";
                
            LogEvent(EventLogEntryType.Warning, 3001,
                $"Dispatcher Queue Failure in {operation}",
                $"Operation: {operation}\n" +
                $"Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}\n" +
                $"Error: {message}\n" +
                $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        /// <summary>
        /// Log service initialization failures
        /// </summary>
        public static void LogServiceInitializationFailure(string serviceName, Exception ex)
        {
            LogEvent(EventLogEntryType.Error, 4001,
                $"Service Initialization Failed: {serviceName}",
                $"Service: {serviceName}\n" +
                $"Exception: {ex.GetType().Name}\n" +
                $"Message: {ex.Message}\n" +
                $"Stack Trace: {ex.StackTrace}");
        }

        /// <summary>
        /// Log application shutdown
        /// </summary>
        public static void LogShutdown(bool isGraceful = true)
        {
            var eventType = isGraceful ? EventLogEntryType.Information : EventLogEntryType.Warning;
            var shutdownType = isGraceful ? "Graceful" : "Unexpected";
            
            LogEvent(eventType, 1002,
                $"{shutdownType} Application Shutdown",
                $"Shutdown Type: {shutdownType}\n" +
                $"Process ID: {Environment.ProcessId}\n" +
                $"Shutdown Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"Uptime: {DateTime.Now - Process.GetCurrentProcess().StartTime}");
        }

        /// <summary>
        /// Log critical application crashes
        /// </summary>
        public static void LogCriticalCrash(Exception ex, string context = "Unknown")
        {
            LogEvent(EventLogEntryType.Error, 5001,
                "Critical Application Crash",
                $"Context: {context}\n" +
                $"Exception Type: {ex.GetType().Name}\n" +
                $"Message: {ex.Message}\n" +
                $"Stack Trace: {ex.StackTrace}\n" +
                $"Process ID: {Environment.ProcessId}\n" +
                $"Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}\n" +
                $"Crash Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        /// <summary>
        /// Log operation performance issues
        /// </summary>
        public static void LogPerformanceIssue(string operation, TimeSpan duration, string details = "")
        {
            LogEvent(EventLogEntryType.Warning, 6001,
                $"Performance Issue: {operation}",
                $"Operation: {operation}\n" +
                $"Duration: {duration.TotalMilliseconds:F0}ms\n" +
                $"Details: {details}\n" +
                $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        /// <summary>
        /// Log Windows Runtime errors specifically
        /// </summary>
        public static void LogWinRTError(Exception ex, string operation)
        {
            var hresult = ex.HResult;
            var errorCode = $"0x{hresult:X8}";
            
            LogEvent(EventLogEntryType.Error, 7001,
                $"Windows Runtime Error in {operation}",
                $"Operation: {operation}\n" +
                $"HRESULT: {errorCode}\n" +
                $"Exception Type: {ex.GetType().Name}\n" +
                $"Message: {ex.Message}\n" +
                $"Inner Exception: {ex.InnerException?.Message ?? "None"}\n" +
                $"Stack Trace: {ex.StackTrace}");
        }

        /// <summary>
        /// Log a custom event with specified details
        /// </summary>
        public static void LogEvent(EventLogEntryType eventType, int eventId, string message, string details)
        {
            try
            {
                if (_eventLog != null)
                {
                    var fullMessage = $"{message}\n\nDetails:\n{details}";
                    _eventLog.WriteEntry(fullMessage, eventType, eventId);
                }
                
                // Always write to debug output as backup
                Debug.WriteLine($"[{eventType}] {eventId}: {message}");
                Debug.WriteLine($"Details: {details}");
            }
            catch (Exception ex)
            {
                // Last resort: debug output only
                Debug.WriteLine($"Event logging failed: {ex.Message}");
                Debug.WriteLine($"Original event: [{eventType}] {eventId}: {message}");
            }
        }

        private static void LogEventInternal(EventLogEntryType eventType, int eventId, string message, string details)
        {
            LogEvent(eventType, eventId, message, details);
        }

        /// <summary>
        /// Dispose of event log resources
        /// </summary>
        public static void Dispose()
        {
            try
            {
                _eventLog?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disposing event log: {ex.Message}");
            }
        }
    }
}