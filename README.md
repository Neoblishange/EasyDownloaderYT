# EasyDownloaderYT

EasyDownloaderYT is a Windows desktop application for downloading supported media as MP4 video or MP3/WAV audio. It is built with .NET 10 and WPF.

EasyDownloaderYT began as a personal project for my father. I wanted to make downloading media straightforward and quick for someone who is not familiar with computers, without having to navigate ad-heavy websites, intrusive pop-ups, or unwanted redirects. I also added a Khmer translation, his usual language, so the app would be easier for him to understand and use.

The application is designed without built-in ads or promotional pop-ups and redirects. Personal use is free. This does not guarantee the safety of third-party tools, websites, or downloaded content.

## License and use

EasyDownloaderYT is for personal, non-commercial use only. Commercializing this project, including selling it or using it for commercial purposes, is prohibited without the copyright holder's prior written permission. See [LICENSE.md](LICENSE.md). Third-party components remain subject to their own licenses.

## Requirements

- Windows
- .NET 10 SDK to build and test

The build downloads any missing pinned versions of yt-dlp, FFmpeg/ffprobe, and Deno into `Resources/Tools` and verifies their SHA-256 hashes. The first build therefore requires an internet connection. The exact versions and redistribution checks are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Build, test, and publish

```powershell
dotnet build EasyDownloaderYT.csproj --configuration Release
dotnet test Tests/EasyDownloaderYT.Tests/EasyDownloaderYT.Tests.csproj --configuration Release
dotnet publish EasyDownloaderYT.csproj --configuration Release --runtime win-x64 --self-contained true --output .\artifacts\publish
```

The publish output is a self-contained, single-file `win-x64` application. The external tools remain beside the executable under `Resources/Tools`.

## Architecture

- `Views/`: the main WPF window and view-specific resources. `Views/Queue/` owns the download queue item template.
- `ViewModels/`: observable screen and queue state, plus commands used by the queue template.
- `Services/Downloads/`: download orchestration, external-process execution, yt-dlp output parsing, error mapping, and FFmpeg conversion.
- `Services/Localization/`: language selection and resource dictionary management.
- `Services/Settings/`: local JSON settings persistence.
- `Resources/Languages/`: translated interface strings. The test suite checks that all dictionaries expose the same keys.
- `Resources/Fonts/`, `Resources/Icons/`, `Resources/Tools/`: bundled application assets and third-party executables.
- `Tests/`: unit tests for queue behavior, download parsing/orchestration, settings, and localization resources. External processes are replaced with a test runner.

The UI submits HTTP(S) links to the view model. yt-dlp resolves the title and downloads one item at a time; FFmpeg handles audio conversion when yt-dlp cannot directly extract MP3. Downloads are saved in the current user's Videos folder under `EasyDownloaderYT`. Queue entries can be cancelled, retried after failure, or cleared when finished.

Supported sites and formats depend on the bundled yt-dlp version. The app does not configure account credentials or browser cookies, so content requiring authentication may not be downloadable.

## Releases

CI builds and tests the project, publishes the self-contained Windows package, verifies that the executable and bundled tools are present, and uploads the package as a workflow artifact. Before distributing a release, review the [third-party notices and release checklist](THIRD-PARTY-NOTICES.md), package all required license notices, and retain the exact binary versions and corresponding source/build information.
