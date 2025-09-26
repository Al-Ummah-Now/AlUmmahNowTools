# AlUmmahNow Visual Importer

## Setup Instructions

### 1. Configure User Secrets

The application requires configuration through user secrets. Use the following commands:

```bash
cd AlUmmahNow.VisualImporter
dotnet user-secrets set "R2DB:S3Api" "https://your-cloudflare-r2-endpoint"
dotnet user-secrets set "R2DB:S3Key" "your-r2-access-key"
dotnet user-secrets set "R2DB:S3Secret" "your-r2-secret-key"
dotnet user-secrets set "BunnyVideo:Zone" "your-bunny-video-zone"
dotnet user-secrets set "BunnyVideo:Key" "your-bunny-video-key"
dotnet user-secrets set "BunnyVideo:Region" "your-bunny-region"
dotnet user-secrets set "BunnyImage:Zone" "your-bunny-image-zone"
dotnet user-secrets set "BunnyImage:Key" "your-bunny-image-key"
dotnet user-secrets set "BunnyImage:Region" "your-bunny-region"
dotnet user-secrets set "BunnyStream:Key" "your-bunny-stream-key"
```

### 2. Run the Application

```bash
cd AlUmmahNow.VisualImporter
dotnet run
```

## Features

- **Visual Interface**: Browse and select folders containing videos
- **Import Tracking**: Real-time progress monitoring with database persistence
- **Session Management**: View, resume, and cancel import operations
- **Multi-Channel Support**: Automatically detects channel subfolders
- **Video Status**: Track individual video upload status and errors
- **Progress Visualization**: Channel-level and overall progress tracking

## Database

The application uses SQLite to store import session data locally in:
`%LocalAppData%\AlUmmahNowVisualImporter\imports.db`

## Supported Video Formats

- MP4 (.mp4)
- MKV (.mkv) 
- AVI (.avi)
- MOV (.mov)
- WMV (.wmv)

## Folder Structure

The importer expects a folder structure like:

```
Root Import Folder/
??? Channel 1/
?   ??? video1.mp4
?   ??? video1.jpg (optional thumbnail)
?   ??? video2.mp4
??? Channel 2/
?   ??? video3.mp4
?   ??? .thumbs/
?       ??? video3.jpg
??? Channel 3/
    ??? video4.mp4
```