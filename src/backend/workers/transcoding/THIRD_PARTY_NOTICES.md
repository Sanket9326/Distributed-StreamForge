# Third-party notices

The Transcoding worker container installs the Alpine Linux `ffmpeg` package,
version `8.0.1-r1` from the Alpine 3.23 community repository. The package
provides `ffmpeg`, `ffprobe`, and the `libx264` encoder used by this service.

FFmpeg is distributed under LGPL 2.1-or-later and, when GPL components such as
libx264 are enabled, GPL 2.0-or-later. The Alpine package metadata and source
provenance are available from:

- <https://pkgs.alpinelinux.org/packages?branch=v3.23&name=ffmpeg>
- <https://gitlab.alpinelinux.org/alpine/aports/-/tree/3.23-stable/community/ffmpeg>
- <https://ffmpeg.org/legal.html>

This notice is informational and does not change the MIT license of the
StreamForge source code. Redistributors are responsible for satisfying the
licenses of the binaries included in their container distribution.
