param(
    [int]$TimeoutMinutes = 45
)

$unityExe = "C:\Program Files\Unity 6000.3.28f1\Editor\Unity.exe"
$projectPath = "C:\Users\AAKASH\Desktop\3D\CoachingPropHunt"
$logFile = "$projectPath\build_apk.log"

Write-Host "=========================================================="
Write-Host "  Parishram Hunt - Full Android ARM64 IL2CPP Build"
Write-Host "  Unity: $unityExe"
Write-Host "  Project: $projectPath"
Write-Host "  Log: $logFile"
Write-Host "=========================================================="

# Remove previous log to ensure fresh tracking
if (Test-Path $logFile) {
    Remove-Item $logFile -Force -ErrorAction SilentlyContinue
}

$argList = @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", $projectPath,
    "-executeMethod", "CoachingPropHunt.Editor.AndroidApkBuilder.BuildApk",
    "-logFile", $logFile
)

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $unityExe -ArgumentList $argList -PassThru

Write-Host "Build process started with PID: $($proc.Id) at $(Get-Date -Format 'HH:mm:ss')"

$timeoutMs = $TimeoutMinutes * 60 * 1000
$finished = $proc.WaitForExit($timeoutMs)
$sw.Stop()

Write-Host "Build process exited in $([int]$sw.Elapsed.TotalMinutes)m $($sw.Elapsed.Seconds)s with code: $($proc.ExitCode)"

$apkPath = "$projectPath\Builds\ParishramHunt.apk"
if (Test-Path $apkPath) {
    $apkItem = Get-Item $apkPath
    Write-Host "----------------------------------------------------------"
    Write-Host "  APK File: $($apkItem.FullName)"
    Write-Host "  Size: $([math]::Round($apkItem.Length / 1MB, 2)) MB ($($apkItem.Length) bytes)"
    Write-Host "  Last Modified: $($apkItem.LastWriteTime)"
    Write-Host "----------------------------------------------------------"
}

if (Test-Path $logFile) {
    Write-Host "`nLast 25 lines of build log:"
    Get-Content $logFile -Tail 25
}
