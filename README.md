# MyTts - Text-to-Speech Service

## Overview

MyTts is a .NET Core application designed to provide Text-to-Speech (TTS) functionalities. It can convert text into speech, merge multiple MP3 files, and stream audio. The service integrates with external TTS providers like Google Cloud TTS and ElevenLabs, and uses FFmpeg for audio processing tasks.

## Main Features

*   **Text-to-Speech Conversion:** Converts input text into speech using configured TTS providers.
*   **MP3 Merging:** Can merge multiple MP3 files (from storage or direct uploads) into a single audio stream.
*   **Audio Streaming:** Streams generated or merged audio content.
*   **User Authentication:** Includes JWT-based authentication and role management (Admin, User).
*   **Database Integration:** Uses Entity Framework Core for storing metadata related to MP3 files and news articles.
*   **Caching:** Leverages Redis for caching to improve performance.
*   **News Integration:** (Partially implemented) Aims to fetch news articles and convert them to audio.

## Technologies Used

*   .NET Core (ASP.NET Core for API)
*   Entity Framework Core (for database interaction)
*   FFmpeg (for audio processing like merging and format conversion)
*   Google Cloud TTS (as a TTS provider)
*   ElevenLabs (as a TTS provider)
*   Redis (for caching)
*   ASP.NET Core Identity (for authentication)
*   Serilog (for logging, based on `LoggingConfig.cs`)
*   AutoMapper

## Setup and Configuration

### Prerequisites

1.  **.NET SDK:** Ensure you have the .NET SDK installed (version compatible with the project, likely .NET 6.0 or newer).
2.  **FFmpeg:** FFmpeg is required for audio processing.
    *   The application expects FFmpeg binaries (`ffmpeg.exe`, `ffprobe.exe`) to be located in an `ffmpeg-bin` directory within the application's base directory at runtime.
    *   You can download FFmpeg from [https://ffmpeg.org/download.html](https://ffmpeg.org/download.html).
    *   After downloading, create a folder named `ffmpeg-bin` in the project's output directory (e.g., `bin/Debug/netX.X/ffmpeg-bin/`) and place `ffmpeg.exe` and `ffprobe.exe` (and `ffplay.exe` if needed) into it. The project includes an `ffmpeg-bin` directory at the root, which should be copied to the output directory during the build process if not already configured.

### Configuration Files

The main configuration file is `appsettings.json`. You may also use environment-specific versions like `appsettings.Development.json`.

Key configurations to review and set up:

*   **Database Connection Strings:**
    *   `ConnectionStrings:DefaultConnection` (for `AppDbContext`)
    *   `ConnectionStrings:AuthConnection` (for `AuthDbContext`)
    *   `ConnectionStrings:DunyaConnection` (for `DunyaDbContext`)
*   **TTS Provider Keys:**
    *   `ElevenLabs:ApiKey`
    *   `GoogleCloudTts:JsonCredentialsPath` (path to your Google Cloud service account JSON key file)
*   **Redis Configuration:**
    *   `Redis:ConnectionString`
*   **Storage Configuration:**
    *   `Storage:LocalStoragePath` (for local file storage)
    *   Cloud storage options if configured (e.g., Azure Blob Storage, AWS S3 - currently, the primary focus seems to be local storage based on `LocalStorageClient.cs`).
*   **Admin User:**
    *   `AdminUser:Email`
    *   `AdminUser:Password` (used for seeding the initial admin user)
*   **JWT Settings:**
    *   `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience`, `Jwt:DurationInMinutes`

### Running the Application

1.  **Restore Dependencies:**
    ```bash
    dotnet restore
    ```
2.  **Build the Application:**
    ```bash
    dotnet build
    ```
    Ensure the `ffmpeg-bin` directory is correctly copied to the output folder (e.g., `MyTts/bin/Debug/net8.0/ffmpeg-bin/`). You might need to adjust the `.csproj` file to ensure these files are copied. The existing `ffmpeg-bin` at the root with `.exe` files suggests it's intended to be available.
3.  **Update Database:**
    ```bash
    dotnet ef database update --context AppDbContext
    dotnet ef database update --context AuthDbContext
    # If DunyaDbContext is used and has migrations:
    # dotnet ef database update --context DunyaDbContext
    ```
4.  **Run the Application:**
    ```bash
    dotnet run
    ```
    The application will typically start on `http://localhost:5000` or `https://localhost:5001`, but check the console output for the exact URLs.

## API Endpoints

The application exposes various API endpoints. Key groups include:

*   `/api/auth/...`: For user registration, login, and management.
*   `/api/mp3/...` or `/api/tts/...` (exact routes defined in `Routes/ApiRoutes.cs`): For TTS conversion, streaming, and MP3 operations.
*   `/api/FullStreamingMp3/...`: For merging and streaming MP3 files.

Refer to `Routes/ApiRoutes.cs` and `Routes/AuthApiRoutes.cs` for detailed route definitions.

## Further Development & Considerations

*   **Implement `NewsFeedsService`:** The news fetching logic is currently a placeholder.
*   **Testing:** Add unit and integration tests.
*   **Security Hardening:** Conduct a thorough security review.
*   **Cloud Storage Implementation:** Flesh out cloud storage options if required.
*   **Detailed API Documentation:** Consider using Swagger/OpenAPI for comprehensive API documentation (initial setup for OpenAPI seems present).
```
