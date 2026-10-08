$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root 'dist/Plutonium-moderation-handoff.zip'
New-Item -ItemType Directory -Force -Path (Join-Path $root 'dist') | Out-Null
$files = @('moderation/package.json','moderation/package-lock.json','moderation/vercel.json','moderation/schema.sql','moderation/.env.example','moderation/.gitignore','moderation/.vercelignore','docs/moderation-setup.md','docs/moderation-api.md','docs/feature-ideas.md')
foreach ($directory in @('api','lib','scripts','test','website','public')) {
    $files += Get-ChildItem -LiteralPath (Join-Path $root "moderation/$directory") -File -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($root,$_.FullName) }
}
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($target,[IO.FileMode]::Create)
$zip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $entry = $zip.CreateEntry($file.Replace('\','/'))
        $output = $entry.Open(); $input = [IO.File]::OpenRead((Join-Path $root $file))
        try { $input.CopyTo($output) } finally { $input.Dispose(); $output.Dispose() }
    }
} finally { $zip.Dispose(); $stream.Dispose() }
Write-Output "Prepared $target with $($files.Count) source/documentation files. No local credentials or dependencies included."
