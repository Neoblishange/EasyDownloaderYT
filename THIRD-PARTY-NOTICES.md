# Third-party components and release checklist

The versions below were read from the binaries currently bundled in `Resources/Tools`. Recheck them whenever a bundled executable or font is replaced. This inventory is not a substitute for checking the exact license files and build options packaged with each release.

| Component | Bundled version/build | License and source |
|---|---|---|
| yt-dlp | `2026.10.26` | Unlicense; https://github.com/yt-dlp/yt-dlp |
| FFmpeg / ffprobe | `8.1.2-essentials_build-www.gyan.dev`; build enables `--enable-gpl` and `--enable-version3` | GPL-3.0-or-later for this build; the enabled codecs and libraries may have additional obligations. Build source and license information: https://www.gyan.dev/ffmpeg/builds/ and https://ffmpeg.org/legal.html |
| Deno | `2.9.8` | MIT; https://github.com/denoland/deno |
| Kantumruy Pro fonts | Bundled TTF files; exact upstream revision not recorded | SIL Open Font License 1.1; upstream project: https://github.com/google/fonts/tree/main/ofl/kantumruypro |

## Before redistributing

- Confirm that each binary still matches the version and license recorded above; retain a checksum and the upstream download/source URL for the release.
- Include the required license and copyright notices for yt-dlp, Deno, the fonts, FFmpeg, and every library enabled in the FFmpeg build.
- For the bundled GPL FFmpeg build, obtain and retain the corresponding source and build configuration, and satisfy the GPL obligations that apply to the distribution method. Do not assume that a link to the general FFmpeg website is sufficient.
- Review the yt-dlp, Deno, and font license files from the exact bundled revisions before packaging.
- Re-run the Windows publish workflow and verify that the package contains the expected tools and notices.

The repository now includes the upstream license texts and legal notices for the bundled components under `Licenses/`. For release packaging, also retain the exact FFmpeg source archive and configure/build information corresponding to the bundled binaries, and keep the final release package aligned with the license obligations of the exact distributed files.

## Included license texts

- `Licenses/yt-dlp-UNLICENSE.txt`
- `Licenses/deno-MIT.txt`
- `Licenses/kantumruypro-OFL.txt`
- `Licenses/ffmpeg-GPLv3.txt`
- `Licenses/ffmpeg-LGPLv2.1.txt`
- `Licenses/ffmpeg-legal-notice.txt`

These files are copied into the build output and publish output by the project file so they travel with the packaged application. They are reference texts for redistribution review; they do not replace the requirement to keep the exact FFmpeg source/build configuration for the release.
