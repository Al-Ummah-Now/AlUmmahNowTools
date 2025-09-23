# Multi-Channel Video Import System

This enhanced video import system supports recursive scanning of multiple channel folders with local SQLite database tracking for progress monitoring and resume capabilities.

## Features

- **Multi-Channel Support**: Automatically detects channel folders and creates separate upload paths for each
- **Recursive Scanning**: Scans all subdirectories within each channel folder for videos
- **Progress Tracking**: Real-time progress monitoring with detailed statistics
- **Resume Capability**: Pause and resume imports without losing progress
- **Local Database**: SQLite database tracks all import sessions, channels, and videos
- **Duplicate Detection**: Skips already uploaded videos to avoid duplicates
- **Comprehensive Logging**: Detailed logs of all operations and errors

## Folder Structure

The system expects the following folder structure:

```
Root Import Folder/
??? Channel 1/
?   ??? video1.mp4
?   ??? video1.jpg (optional thumbnail)
?   ??? Subfolder/
?   ?   ??? video2.mkv
?   ?   ??? video2.png
?   ??? .thumbs/ (optional thumbnails folder)
?       ??? video3.jpg
??? Channel 2/
?   ??? video4.mp4
?   ??? video5.avi
??? Channel 3/
    ??? Series/
        ??? Episode 1/
        ?   ??? episode1.mp4
        ??? Episode 2/
            ??? episode2.mp4
```

## Usage

### Start New Multi-Channel Import

```bash
dotnet run -- --folder "C:\Videos\Channels" --user "user123" --video-url "https://cdn.example.com" --image-url "https://images.example.com"
```

### Resume Interrupted Import

```bash
dotnet run -- --resume "session-guid-here"
```

### Check Import Status

```bash
dotnet run -- --status "session-guid-here"
```

### Command Line Options

- `-f|--folder <path>`: Root folder containing channel subdirectories (required for new imports)
- `-u|--user <userId>`: User ID for the import (optional, generates GUID if not provided)
- `--video-url <url>`: Base URL for video storage (optional, defaults to https://cdn.alummahnow.net)
- `--image-url <url>`: Base URL for image storage (optional, defaults to https://images.alummahnow.net)
- `--resume <sessionId>`: Resume a previous import session
- `--status <sessionId>`: Get status of an import session

## Database Schema

The system uses SQLite database (`importdb.sqlite`) with the following tables:

### ImportSessions
- Tracks overall import sessions
- Contains progress statistics and status

### ImportChannels  
- One record per channel (folder)
- Contains channel-specific configuration and progress

### ImportVideos
- One record per video file
- Tracks individual video processing status

## Import Process

1. **Discovery Phase**: Recursively scans all channel folders for video files
2. **Database Setup**: Creates records for session, channels, and videos
3. **Processing Phase**: Uploads videos and thumbnails with progress tracking
4. **Metadata Storage**: Saves video information to both local and remote storage

## Video File Support

Supported video formats:
- `.mp4`
- `.mkv` 
- `.avi`
- `.mov`
- `.wmv`

Supported thumbnail formats:
- `.jpg`
- `.jpeg`
- `.png`
- `.webp`

## Channel Organization

Each channel folder creates:
- **Video Storage Path**: `/videos-{channelname}/{channelname}/`
- **Image Storage Path**: `/images-{channelname}/`
- **Video URL**: `{baseVideoUrl}/{channelname}/`
- **Image URL**: `{baseImageUrl}/{channelname}/`

## Error Handling & Resume

- Failed uploads are tracked with error messages and retry counts
- System can be safely interrupted and resumed later
- Only pending and failed videos are processed during resume
- Duplicate detection prevents re-uploading existing videos

## Status Monitoring

The status command provides detailed information:
- Overall session progress percentage
- Per-channel progress statistics
- Upload success/failure counts
- Error messages for failed operations

## Configuration

Update `appsettings.json` with your service credentials:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=importdb.sqlite"
  },
  "R2DB": {
    "S3Api": "https://your-r2-endpoint",
    "S3Key": "your-access-key",
    "S3Secret": "your-secret-key"
  },
  "BunnyVideo": {
    "Zone": "your-video-zone",
    "Key": "your-video-key", 
    "Region": "your-region"
  },
  "BunnyImage": {
    "Zone": "your-image-zone",
    "Key": "your-image-key",
    "Region": "your-region"
  }
}
```

## Example Output

```
Starting multi-channel import from: C:\Videos\Channels
Found 3 potential channels
Found 15 videos in channel: Tech Reviews
Found 8 videos in channel: Gaming
Found 12 videos in channel: Tutorials

Processing videos across all channels
Processing Tech Reviews ???????????????????? 100%
Processing Gaming      ???????????????????? 100% 
Processing Tutorials   ???????????????????? 100%

Import completed. Session ID: a1b2c3d4-e5f6-7890-abcd-ef1234567890
Final Status: Completed
Total Videos: 35
Uploaded: 33
Failed: 2
```