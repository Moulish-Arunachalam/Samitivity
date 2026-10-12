# Synthetic fixtures

`synthetic-mono-22050.wav` and `synthetic-stereo-48000.wav` are half-second, low-amplitude sine tones with a short edge ramp, generated specifically for this project. They contain no voice or private information and may be used without restriction for testing. Start with low speaker volume. Windows integration tests also synthesize PCM and encode temporary AAC/M4A and MP3 using Media Foundation; no FFmpeg or personal fixtures are needed.

`config.example.json` mirrors the default configuration. Modifier bits are Alt=1, Ctrl=2, Shift=4, Win=8. Key 83 is virtual S; key 8 is Backspace. Use the UI to edit real configuration and validate hotkey registration. Do not copy this example over an existing personalized library configuration.
