# Samitivity polish and storage migration

The active data root is `%LOCALAPPDATA%\Samitivity`. Product, installer, diagnostics and documentation now use Samitivity. The only old product-name literal in production code is `LittleNotes`, required to locate an existing installation's data.

## Migration

On the first normal application launch, under the existing single-instance mutex and before opening stores, files from `%LOCALAPPDATA%\LittleNotes` are copied to the new root. Close older versions before upgrading. Installer/startup helper commands do not migrate or load the library.

- Neither folder exists: create the new root normally.
- Only the old folder exists: copy settings, history, index, recordings, Trash, backups and other files, preserving timestamps. Keep the old folder untouched as a recovery backup.
- Only the new folder exists: use it unchanged.
- Both exist: copy missing files; skip byte-identical files. Current files win. Differing recordings with identical paths receive deterministic content-hash names in the same category folder. Other differing files, including configuration and history, are preserved under `Samitivity\RecoveredData` with their relative paths. No file is overwritten.

Conflicting configurations/history are deliberately not combined: silently changing shortcuts or interleaving shuffle history would be surprising. The app displays a recovery notice if RecoveredData exists. Categories present only in the old configuration remain on disk but are not automatically added to the current category list. Read `RecoveredData\config.json` to identify those category IDs and import recordings from their `VoiceNotes\<ID without hyphens>` folders into the desired current categories. Conflicting history remains recoverable in RecoveredData; current history remains active. This is the exceptional two-populated-libraries case, not the normal upgrade path.

Each copy is flushed to a temporary sibling then renamed without replacement. A failure prevents normal startup and leaves the source intact; retry after fixing permissions/free space. Completed files are recognized on retry. Links/junctions are rejected. Completion is recorded in `storage-migration-v1.txt`; subsequent starts do not reimport the backup. A hard process kill may leave a `.migration-*` temporary file, which can be removed after checking the completed migration. The backup intentionally consumes extra disk space; remove it manually only after verifying the new library. Uninstall preserves both locations.

## Files copied outside the app

Save a category, select it, and choose **Open folder** to find its managed directory. Category IDs remain stable across renames. Copy `.wav`, `.mp3` or `.m4a` files directly into that directory (not a named sibling or nested subfolder), then choose **Refresh**. Refresh scans all saved categories and forces audio revalidation, even when size/timestamp are unchanged. Valid copies are indexed and persist across restart; duplicate content appears once per category. Original filenames and bytes remain intact.

The existing watcher still debounces file changes. Failed validation is retried up to three times with a two-second delay, allowing an in-progress copy to finish. Opening Configure and saving configuration also rescan. Watcher errors request another scan. Files changed during validation are rejected until a later scan; invalid/empty/oversize files and durations outside the import limits produce visible warnings. Finish a long copy and Refresh if retries have elapsed. Unsaved categories must be saved before Open folder is available.

CrossOver filesystem notifications and Windows audio decoder availability may differ from native Windows; neither is established as the original cause. Explicit Refresh bypasses notification dependence, but cannot supply missing codecs or make corrupt audio playable. Real Windows remains the supported validation target.

## Appearance and validation

The window has a fixed 860×640 logical client area, no maximize/resize, a local embedded rose background, a 20-pixel exposed border, and a rounded, softened, warm glass panel. Burgundy buttons and selections, Segoe Script heading and Segoe UI body text keep the native controls coherent. Glass is cached GDI+ rendering, not desktop transparency or a web surface. The supplied artwork is embedded locally; there are no network requests. Windows applies normal DPI scaling.

Portable tests cover migration preservation, collision handling, idempotent retries, linked-path rejection, direct file detection, same-metadata forced refresh and duration limits. Windows tests exercise real WAV decoding/preparation after direct copy and painting the embedded rose surface. Still check actual Windows laptop layout at 100%, 125% and 150% DPI, keyboard focus, scrolling and audible playback; automated rendering is not a visual usability certification. CrossOver rendering and watcher delivery require a separate manual check.
