# Code maintenance

The client, launcher, moderation service and release tooling share formatting conventions in `.editorconfig`. Java uses four-space AOSP formatting; C# uses expanded blocks; JavaScript uses the checked-in Prettier configuration. Format source and tests, not generated code, downloaded dependencies or game assets.

This cleanup used google-java-format 1.24.0 (AOSP, with import changes and long-string rewriting disabled), the .NET SDK's C# formatter, Prettier 3.6.2 and PSScriptAnalyzer 1.25.0. Before the error-handling edits, Java class bytecode was compared with debug locations removed, C# syntax tokens were compared, and formatting-only JavaScript syntax trees and PowerShell tokens were checked. These checks supplement the tests; they do not replace them.

## Error handling conventions

- Handle an error where the code can recover or give the user useful context. Preserve cancellation and rethrow with the original stack when recovery is not possible.
- `ClientDiagnostics` accepts fixed operation descriptions and exception types. It rate-limits repeated warnings and deliberately omits exception messages, credentials, server text and settings values. Invalid individual settings keep their existing fallback without discarding valid neighboring settings.
- `TemporaryCleanup` is only for disposable staging files/directories. Locked temporary files are left in place with a diagnostic rather than replacing the original error. Do not use it for user files, uninstall actions, committed mods or rollback copies that must be retained.
- Failed mod recovery preserves the staging directory and includes both the install and recovery exceptions. Launcher error reports include nested exceptions and pass through credential/path redaction before display or sharing.
- `withTransaction` owns BEGIN/COMMIT/ROLLBACK and connection release. Callers retain their authorization checks and row locks inside the callback. Failed rollback connections are discarded; rollback/release errors cannot overwrite an existing operation error. Logs contain fixed event names, never SQL values, connection strings or authentication payloads.
- Keep optional integration failures separate from core startup failures. Do not introduce broad success fallbacks for authentication, permissions, checksums or file validation.

## Verification

Run the Java tests and both client builds, launcher tests, and `npm test` in `moderation`. For changes to rendering, input or game hooks, run the existing isolated standalone and Fabric smoke checks. Offline test fixtures can use a separate unprovisioned service configuration; never package that fixture configuration into a release.

Before publishing, verify the client and launcher version manifests, production moderation endpoint, and SHA-256 hashes of every uploaded asset. API source changes are included in the moderation handoff archive; publishing a GitHub release does not itself deploy Vercel.
