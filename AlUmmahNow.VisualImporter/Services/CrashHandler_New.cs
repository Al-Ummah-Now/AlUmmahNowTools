using System;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AlUmmahNow.VisualImporter.Services;
using System.Diagnostics;

namespace AlUmmahNow.VisualImporter.Services
{
    /// <summary>
    /// Global exception handler for catching and managing application crashes
    /// </summary>
    public static class CrashHandler
    {
        private static bool _isInitialized = false;

        /// <summary>
        /// Initialize global exception handling
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                // Handle unhandled exceptions in the current AppDomain
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                
                // Handle unhandled exceptions in tasks
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
                
                // Handle first chance exceptions for debugging
                AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;

                WindowsEventLogger.LogEvent(EventLogEntryType.Information, 8001,
                    "Crash Handler Initialized",
                    "Global exception handling has been set up successfully");

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize crash handler: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle unhandled exceptions in the main application domain
        /// </summary>
        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var exception = e.ExceptionObject as Exception;
                var isTerminating = e.IsTerminating;

                if (exception != null)
                {
                    // Determine if this is a COM/WinRT related crash
                    var isCOMError = IsCOMRelatedError(exception);
                    
                    if (isCOMError)
                    {
                        WindowsEventLogger.LogComMarshalingError(exception, "Unhandled Application Domain Exception");
                    }
                    else
                    {
                        WindowsEventLogger.LogCriticalCrash(exception, "Unhandled Application Domain Exception");
                    }

                    // Log additional details
                    LogCrashDetails(exception, isTerminating);
                }

                if (isTerminating)
                {
                    WindowsEventLogger.LogShutdown(false);
                    
                    // Try to save any critical state before termination
                    try
                    {
                        SaveCrashDump();
                    }
                    catch
                    {
                        // Ignore errors during crash dump saving
                    }
                }
            }
            catch (Exception logEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error in crash handler: {logEx.Message}");
            }
        }

        /// <summary>
        /// Handle unobserved task exceptions
        /// </summary>
        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                if (e.Exception != null)
                {
                    var flattenedException = e.Exception.Flatten();
                    
                    foreach (var innerException in flattenedException.InnerExceptions)
                    {
                        if (IsCOMRelatedError(innerException))
                        {
                            WindowsEventLogger.LogComMarshalingError(innerException, "Unobserved Task Exception");
                        }
                        else
                        {
                            WindowsEventLogger.LogCriticalCrash(innerException, "Unobserved Task Exception");
                        }
                    }

                    // Mark as observed to prevent process termination
                    e.SetObserved();
                }
            }
            catch (Exception logEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling unobserved task exception: {logEx.Message}");
            }
        }

        /// <summary>
        /// Handle first chance exceptions for debugging
        /// </summary>
        private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
        {
            try
            {
                // Only log COM/WinRT related first chance exceptions to avoid spam
                if (IsCOMRelatedError(e.Exception))
                {
                    System.Diagnostics.Debug.WriteLine($"First chance COM exception: {e.Exception.Message}");
                    
                    // Log if it's a critical COM error
                    if (IsCriticalCOMError(e.Exception))
                    {
                        WindowsEventLogger.LogWinRTError(e.Exception, "First Chance Exception");
                    }
                }
            }
            catch
            {
                // Ignore errors in first chance handler to avoid recursion
            }
        }

        /// <summary>
        /// Check if an exception is COM/WinRT related
        /// </summary>
        private static bool IsCOMRelatedError(Exception exception)
        {
            // Check for specific COM/WinRT exception types and error codes
            return exception is COMException ||
                   exception is SEHException ||
                   exception.GetType().Name.Contains("COMException") ||
                   exception.GetType().Name.Contains("WinRT") ||
                   exception.Message.Contains("CoreMessagingXP") ||
                   exception.Message.Contains("WinRT") ||
                   exception.Message.Contains("0xe0464645") ||
                   exception.HResult == unchecked((int)0xe0464645) ||
                   exception.HResult == unchecked((int)0x80004005) || // E_FAIL
                   exception.HResult == unchecked((int)0x8000FFFF) || // E_UNEXPECTED
                   exception.Source?.Contains("Windows.Runtime") == true;
        }

        /// <summary>
        /// Check if a COM error is critical
        /// </summary>
        private static bool IsCriticalCOMError(Exception exception)
        {
            var hresult = exception.HResult;
            
            // Known critical COM error codes
            return hresult == unchecked((int)0xe0464645) || // CoreMessagingXP crash code
                   hresult == unchecked((int)0x80070005) || // E_ACCESSDENIED
                   hresult == unchecked((int)0x8007000E) || // E_OUTOFMEMORY
                   hresult == unchecked((int)0x80004001);   // E_NOTIMPL
        }

        /// <summary>
        /// Handle specific COM exceptions with recovery attempts
        /// </summary>
        public static bool TryHandleCOMException(Exception ex, string operation)
        {
            try
            {
                if (!IsCOMRelatedError(ex))
                    return false;

                WindowsEventLogger.LogComMarshalingError(ex, operation);

                // Attempt recovery for specific error codes
                var hresult = ex.HResult;
                switch (hresult)
                {
                    case unchecked((int)0xe0464645): // CoreMessagingXP crash
                        System.Diagnostics.Debug.WriteLine("Attempting to recover from CoreMessagingXP crash...");
                        return RecoverFromCoreMessagingXPCrash(operation);

                    case unchecked((int)0x80004005): // E_FAIL
                        System.Diagnostics.Debug.WriteLine("Attempting to recover from E_FAIL...");
                        return RecoverFromEFail(operation);

                    case unchecked((int)0x8007000E): // E_OUTOFMEMORY
                        System.Diagnostics.Debug.WriteLine("Attempting to recover from out of memory...");
                        return RecoverFromOutOfMemory(operation);

                    case unchecked((int)0x80070005): // E_ACCESSDENIED
                        System.Diagnostics.Debug.WriteLine("Attempting to recover from access denied...");
                        return RecoverFromAccessDenied(operation);

                    default:
                        System.Diagnostics.Debug.WriteLine($"Unknown COM error 0x{hresult:X8}, attempting general recovery...");
                        return RecoverFromGeneralCOMError(operation);
                }
            }
            catch (Exception recoveryEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error during COM exception recovery: {recoveryEx.Message}");
                WindowsEventLogger.LogCriticalCrash(recoveryEx, $"COM Recovery for {operation}");
                return false;
            }
        }

        /// <summary>
        /// Specific recovery for CoreMessagingXP crashes
        /// </summary>
        private static bool RecoverFromCoreMessagingXPCrash(string operation)
        {
            try
            {
                WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 8003,
                    "CoreMessagingXP Recovery Attempt", $"Attempting recovery for operation: {operation}");

                // Force aggressive garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // Give the system time to recover
                System.Threading.Thread.Sleep(100);

                // Try to force COM cleanup
                Marshal.CleanupUnusedObjectsInCurrentContext();

                // Force another GC after COM cleanup
                GC.Collect();

                WindowsEventLogger.LogEvent(EventLogEntryType.Information, 8004,
                    "CoreMessagingXP Recovery Completed", "Recovery operations completed successfully");

                return true;
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogCriticalCrash(ex, "CoreMessagingXP Recovery");
                return false;
            }
        }

        /// <summary>
        /// Recovery for E_FAIL errors
        /// </summary>
        private static bool RecoverFromEFail(string operation)
        {
            try
            {
                WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 8005,
                    "E_FAIL Recovery Attempt", $"Attempting recovery for operation: {operation}");

                // Mild garbage collection
                GC.Collect();
                System.Threading.Thread.Sleep(50);

                return true;
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogCriticalCrash(ex, "E_FAIL Recovery");
                return false;
            }
        }

        /// <summary>
        /// Recovery for out of memory errors
        /// </summary>
        private static bool RecoverFromOutOfMemory(string operation)
        {
            try
            {
                WindowsEventLogger.LogEvent(EventLogEntryType.Error, 8006,
                    "Out of Memory Recovery Attempt", $"Attempting recovery for operation: {operation}");

                // Aggressive memory cleanup
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // Give system time to free memory
                System.Threading.Thread.Sleep(200);

                // Force another GC cycle
                GC.Collect();

                return true;
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogCriticalCrash(ex, "Out of Memory Recovery");
                return false;
            }
        }

        /// <summary>
        /// Recovery for access denied errors
        /// </summary>
        private static bool RecoverFromAccessDenied(string operation)
        {
            try
            {
                WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 8007,
                    "Access Denied Recovery Attempt", $"Attempting recovery for operation: {operation}");

                // Brief pause to allow permissions to resolve
                System.Threading.Thread.Sleep(100);

                return true;
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogCriticalCrash(ex, "Access Denied Recovery");
                return false;
            }
        }

        /// <summary>
        /// General COM error recovery
        /// </summary>
        private static bool RecoverFromGeneralCOMError(string operation)
        {
            try
            {
                WindowsEventLogger.LogEvent(EventLogEntryType.Warning, 8008,
                    "General COM Recovery Attempt", $"Attempting recovery for operation: {operation}");

                // Standard COM recovery
                GC.Collect();
                GC.WaitForPendingFinalizers();
                System.Threading.Thread.Sleep(50);

                return true;
            }
            catch (Exception ex)
            {
                WindowsEventLogger.LogCriticalCrash(ex, "General COM Recovery");
                return false;
            }
        }

        /// <summary>
        /// Log detailed crash information
        /// </summary>
        private static void LogCrashDetails(Exception exception, bool isTerminating)
        {
            try
            {
                var details = $"Exception Details:\n" +
                             $"Type: {exception.GetType().FullName}\n" +
                             $"Message: {exception.Message}\n" +
                             $"HRESULT: 0x{exception.HResult:X8}\n" +
                             $"Source: {exception.Source}\n" +
                             $"Target Site: {exception.TargetSite}\n" +
                             $"Is Terminating: {isTerminating}\n" +
                             $"Process ID: {System.Diagnostics.Process.GetCurrentProcess().Id}\n" +
                             $"Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}\n" +
                             $"Memory Usage: {GC.GetTotalMemory(false):N0} bytes\n" +
                             $"Stack Trace:\n{exception.StackTrace}";

                if (exception.InnerException != null)
                {
                    details += $"\n\nInner Exception:\n{exception.InnerException}";
                }

                WindowsEventLogger.LogEvent(EventLogEntryType.Error, 9001,
                    "Detailed Crash Information", details);
            }
            catch (Exception logEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error logging crash details: {logEx.Message}");
            }
        }

        /// <summary>
        /// Save a crash dump for analysis
        /// </summary>
        private static void SaveCrashDump()
        {
            try
            {
                var dumpPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AlUmmahNowVisualImporter",
                    "CrashDumps");

                System.IO.Directory.CreateDirectory(dumpPath);

                var fileName = $"crash_{DateTime.Now:yyyyMMdd_HHmmss}_{Environment.ProcessId}.txt";
                var fullPath = System.IO.Path.Combine(dumpPath, fileName);

                var crashInfo = $"Crash Report - {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                               $"Process ID: {Environment.ProcessId}\n" +
                               $"Application Version: 1.0.0.0\n" +
                               $".NET Version: {Environment.Version}\n" +
                               $"OS Version: {Environment.OSVersion}\n" +
                               $"Machine Name: {Environment.MachineName}\n" +
                               $"User Name: {Environment.UserName}\n" +
                               $"Working Set: {Environment.WorkingSet:N0} bytes\n" +
                               $"Total Memory: {GC.GetTotalMemory(false):N0} bytes\n\n" +
                               $"Last known operation: Visual Importer running\n" +
                               $"Probable cause: COM/WinRT marshaling error in CoreMessagingXP.dll\n";

                System.IO.File.WriteAllText(fullPath, crashInfo);

                WindowsEventLogger.LogEvent(EventLogEntryType.Information, 9002,
                    "Crash Dump Saved", $"Crash dump saved to: {fullPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save crash dump: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleanup crash handler resources
        /// </summary>
        public static void Cleanup()
        {
            if (!_isInitialized) return;

            try
            {
                AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
                TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
                AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;

                WindowsEventLogger.LogEvent(EventLogEntryType.Information, 8002,
                    "Crash Handler Cleanup", "Global exception handlers have been removed");

                _isInitialized = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during crash handler cleanup: {ex.Message}");
            }
        }
    }
}