using McMaster.Extensions.CommandLineUtils;

namespace AlUmmahNow.DataImporter.Commands
{
    /// <summary>
    /// Service for validating and parsing command line arguments
    /// </summary>
    public class CommandValidator
    {
        /// <summary>
        /// Validate a session ID string and parse it to Guid
        /// </summary>
        /// <param name="sessionIdValue">Session ID string from command line</param>
        /// <param name="sessionGuid">Parsed Guid if valid</param>
        /// <returns>True if valid, false otherwise</returns>
        public bool ValidateSessionId(string? sessionIdValue, out Guid sessionGuid)
        {
            sessionGuid = Guid.Empty;
            
            if (string.IsNullOrEmpty(sessionIdValue))
            {
                Console.WriteLine("Error: Session ID is required");
                return false;
            }

            if (!Guid.TryParse(sessionIdValue, out sessionGuid))
            {
                Console.WriteLine("Error: Invalid session ID format");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validate folder path parameter
        /// </summary>
        /// <param name="folderPath">Folder path from command line</param>
        /// <returns>True if valid, false otherwise</returns>
        public bool ValidateFolderPath(string? folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                Console.WriteLine("Error: Folder path is required");
                return false;
            }

            if (!Directory.Exists(folderPath))
            {
                Console.WriteLine($"Error: Folder does not exist: {folderPath}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Get scan name from option or derive from folder path
        /// </summary>
        /// <param name="scanNameOption">Scan name option from command line</param>
        /// <param name="folderPath">Folder path to derive name from</param>
        /// <returns>Scan name to use</returns>
        public string GetScanName(CommandOption scanNameOption, string folderPath)
        {
            return scanNameOption.Value() ?? Path.GetFileName(folderPath) ?? "Unnamed Scan";
        }
    }
}