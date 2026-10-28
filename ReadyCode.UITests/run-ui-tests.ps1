<#
.SYNOPSIS
    Runs the UI smoke tests: builds READYCode (Debug), then launches it and drives it through
    UI Automation for each test.

.DESCRIPTION
    The tests send real keystrokes to the app, so don't use the keyboard or mouse until they
    finish (a test fails rather than typing into another window if the app loses focus). Each
    test runs the app against a throwaway settings folder, so your own settings are never
    touched.

.PARAMETER Filter
    Optional dotnet test filter, e.g. -Filter FindBarTests to run one class.

.EXAMPLE
    .\ReadyCode.UITests\run-ui-tests.ps1
.EXAMPLE
    .\ReadyCode.UITests\run-ui-tests.ps1 -Filter ProjectSearchTests
#>
param([string]$Filter)

$repoRoot = Split-Path $PSScriptRoot -Parent
Write-Host "UI smoke tests launch READYCode and send it keystrokes - hands off the keyboard and mouse until they finish." -ForegroundColor Yellow

$env:READYCODE_UI_TESTS = '1'
try {
    $testArgs = @('test', (Join-Path $repoRoot 'ReadyCode.UITests\ReadyCode.UITests.csproj'), '-c', 'Debug', '--logger', 'console;verbosity=normal')
    if ($Filter) { $testArgs += @('--filter', $Filter) }
    & dotnet @testArgs
    exit $LASTEXITCODE
}
finally {
    Remove-Item Env:READYCODE_UI_TESTS -ErrorAction SilentlyContinue
}
