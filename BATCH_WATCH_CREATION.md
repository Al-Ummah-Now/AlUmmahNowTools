# Batch Watch Creation System

## Overview

The Batch Watch Creation system allows users to create multiple file system watches at once using templates or by importing configurations from CSV/JSON files. This significantly reduces the time and effort required to set up multiple watches with similar settings.

## Features

### 1. Template-Based Creation
- **Built-in Templates**: Pre-configured templates for common use cases
  - Numbered Channels: Creates watches like Channel_01, Channel_02, etc.
  - Category-based: Creates watches for News, Sports, Entertainment, etc.
  - Language-based: Creates watches for English, Arabic, French, etc.
  - Date-based Organization: Creates watches organized by YYYY-MM structure

- **Custom Templates**: Save your own watch configurations as reusable templates

- **Variable Support in Templates**:
  - `{number}`: Sequential number (1, 2, 3...)
  - `{number:D2}`: Zero-padded number (01, 02, 03...)
  - `{number:D3}`: Three-digit zero-padded (001, 002, 003...)
  - `{index}`: Zero-based index (0, 1, 2...)
  - `{date}`: Current date (yyyy-MM-dd)
  - `{date:yyyy}`: Year only (2025)
  - `{date:MM}`: Month only (01-12)
  - `{time}`: Current time (HHmmss)
  - `{channel}`: Channel name (useful in path patterns)

### 2. Import from CSV
CSV files allow bulk import of watch configurations with validation.

**Format**:
```csv
ChannelName,WatchPath,ProcessingDelay,IncludeSubdirs,Extensions
Channel1,/videos/ch1,10,true,"mp4,mkv"
Channel2,/videos/ch2,15,false,"mp4"
```

**Fields**:
- `ChannelName`: Name of the channel (required)
- `WatchPath`: Directory to monitor (required)
- `ProcessingDelay`: Delay in seconds before processing detected files (required)
- `IncludeSubdirs`: true/false - monitor subdirectories (required)
- `Extensions`: Comma-separated file extensions in quotes (required)

**Example**: See `example_watch_import.csv` in the project root

### 3. Import from JSON
JSON files provide a more flexible format with full watch configuration support.

**Format**:
```json
[
  {
    "ChannelName": "Islamic Lectures",
    "WatchPath": "C:\\Videos\\Islamic_Lectures",
    "IncludeSubdirectories": true,
    "ProcessingDelayMs": 10000,
    "FileExtensions": ".mp4,.mkv,.avi",
    "CreatedBy": "Admin"
  }
]
```

**Fields**:
- `ChannelName`: Name of the channel (required)
- `WatchPath`: Directory to monitor (required)
- `IncludeSubdirectories`: Boolean - monitor subdirectories (optional, default: true)
- `ProcessingDelayMs`: Delay in milliseconds (optional, default: 5000)
- `FileExtensions`: Comma-separated extensions (optional, default: common video formats)
- `CreatedBy`: User creating the watch (optional)

**Example**: See `example_watch_import.json` in the project root

## Usage

### Creating Watches from Template

1. Click **"Batch Create"** button in Watch Management page
2. Select **"From Template"** tab
3. Choose a template from the dropdown
4. Configure settings:
   - **Number of Watches**: How many watches to create (1-50)
   - **Starting Number**: First number in the sequence (default: 1)
   - **Base Path**: Root directory for all watches
5. Click **"Generate Preview"** to see what will be created
6. Review the preview list showing channel names and paths
7. Click **"Create All Watches"** to create them

**Example**:
- Template: "Numbered Channels"
- Number: 5
- Starting Number: 1
- Base Path: C:\Videos

Result:
- Channel_01 → C:\Videos\Channel_01
- Channel_02 → C:\Videos\Channel_02
- Channel_03 → C:\Videos\Channel_03
- Channel_04 → C:\Videos\Channel_04
- Channel_05 → C:\Videos\Channel_05

### Importing from File

1. Click **"Batch Create"** button in Watch Management page
2. Select **"Import from File"** tab
3. Click **"Browse"** and select your CSV or JSON file
4. Click **"Import and Validate"**
5. Review the validation results:
   - **Green message**: All configurations are valid
   - **Yellow message**: Some configurations have errors (shown below)
6. If valid, click **"Create Valid Watches"** to import them

### Validation

The system validates all watch configurations before creation:
- Channel name must be 2-100 characters
- Watch path must exist on the file system
- Watch path must not already be monitored
- Channel name must not already exist
- Processing delay must be between 1000ms and 300000ms
- File extensions must be provided

Invalid configurations are highlighted with detailed error messages.

### Progress Tracking

During batch creation, you'll see:
- Progress bar showing completion percentage
- Count of watches created (e.g., "Creating: 3/10")
- Status messages for each operation
- Final summary (e.g., "Batch creation complete: 8 successful, 2 failed")

## File Locations

### Templates Storage
User-created templates are stored in:
```
%APPDATA%/AlUmmahNow/WatchTemplates/
```

Each template is saved as a JSON file named with its ID (GUID).

### Built-in Templates
Built-in templates are hardcoded in the application and cannot be modified or deleted.

## API Reference

### WatchTemplateService

```csharp
// Get all templates (built-in + custom)
Task<List<WatchTemplate>> GetTemplatesAsync()

// Generate watches from template
Task<WatchTemplatePreview> GenerateWatchesFromTemplateAsync(
    WatchTemplate template,
    int count,
    int startingNumber = 1)

// Import from CSV
Task<WatchBatchImport> ImportFromCsvAsync(string csvPath)

// Import from JSON
Task<WatchBatchImport> ImportFromJsonAsync(string jsonPath)

// Export to JSON
Task<string> ExportToJsonAsync(
    List<CreateWatchRequest> watches,
    string outputPath)

// Save custom template
Task<WatchTemplate> SaveTemplateAsync(WatchTemplate template)

// Delete custom template
Task<bool> DeleteTemplateAsync(Guid id)
```

### ViewModel Commands

```csharp
// Open batch creation dialog
OpenBatchCreateDialogCommand

// Generate preview from template
GeneratePreviewCommand

// Create watches from template preview
CreateWatchesFromTemplateCommand

// Import from file
ImportWatchesFromFileCommand

// Create imported watches
CreateImportedWatchesCommand

// Load available templates
LoadTemplatesCommand

// Save current configuration as template
SaveAsTemplateCommand
```

## Error Handling

### Common Errors

1. **"File does not exist"**
   - Cause: The watch path doesn't exist on the file system
   - Solution: Create the directory first or correct the path

2. **"A watch for this channel already exists"**
   - Cause: A watch with the same channel name is already configured
   - Solution: Use a different channel name or delete the existing watch

3. **"This path is already being watched"**
   - Cause: Another watch is already monitoring this directory
   - Solution: Use a different path or modify the existing watch

4. **"Not connected to FileScan service"**
   - Cause: The backend FileScan service is not running
   - Solution: Start the FileScan service first

5. **"Invalid CSV header"**
   - Cause: CSV file header doesn't match expected format
   - Solution: Ensure header is: ChannelName,WatchPath,ProcessingDelay,IncludeSubdirs,Extensions

6. **"Processing delay must be a positive integer"**
   - Cause: Invalid processing delay value in CSV
   - Solution: Use whole numbers greater than 0 (seconds)

## Best Practices

1. **Start Small**: Test with 2-3 watches before creating large batches
2. **Verify Paths**: Ensure all watch paths exist before importing
3. **Use Descriptive Names**: Use clear channel names for easy identification
4. **Preview First**: Always generate and review the preview before creating
5. **Monitor Progress**: Watch the progress bar and status messages
6. **Check Results**: Verify that all watches were created successfully
7. **Template Reuse**: Save successful configurations as templates for future use
8. **Backup Configurations**: Export watch configurations to JSON for backup

## Troubleshooting

### Template Variables Not Replaced
- **Issue**: Variables like {number} appear literally in channel names
- **Solution**: Ensure you're using the correct syntax with curly braces

### All Imports Fail
- **Issue**: None of the imported watches are created
- **Solution**: Check file format, paths, and permissions

### Some Watches Created, Some Failed
- **Issue**: Partial success in batch creation
- **Solution**: Check the logs for specific failures, common causes are:
  - Path doesn't exist
  - Permission denied
  - Duplicate channel name

### Progress Bar Stuck
- **Issue**: Progress bar doesn't move during creation
- **Solution**: Check if the service is responding, may need to restart

## Performance Considerations

- **Batch Size**: Creating 50 watches takes approximately 10-30 seconds
- **File System**: Ensure adequate disk space for all watch directories
- **Memory**: Each watch uses minimal memory (< 1MB per watch)
- **CPU**: Batch creation is CPU-light but I/O intensive

## Security

- **Path Validation**: All paths are validated before creation
- **User Attribution**: Watches are tagged with the creating user
- **Audit Trail**: All watch creation is logged
- **No Elevation Required**: Standard user permissions are sufficient

## Future Enhancements

Planned features for future releases:
- Export watches to CSV
- Duplicate existing watch configuration
- Bulk edit existing watches
- Template sharing and import
- Scheduled batch creation
- Auto-create directories if they don't exist
- Watch path variables (e.g., $USER, $DATE)

## Support

For issues or questions about batch watch creation:
1. Check this documentation first
2. Review the example files (example_watch_import.csv, example_watch_import.json)
3. Check the application logs for detailed error messages
4. Verify the FileScan service is running and accessible
