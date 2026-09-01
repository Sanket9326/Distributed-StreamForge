# StreamForge Web

Angular 22 client for the StreamForge home feed and upload flow. Home initially
loads one completed video and requests ten older videos after scrolling. Inline
players select the highest progressive MP4 rendition, defer media loading until
near the viewport, and use native playback/fullscreen controls. Upload remains a
separate route and registers a browser-local completion notification over SSE.

Use the repository [development setup](../../docs/development/setup.md) for the
supported Node version and verified commands.
