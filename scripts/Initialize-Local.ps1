param([string]$DotnetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$stageRoot = Split-Path $PSScriptRoot -Parent
Set-Location $stageRoot
$env:LANDERP_REPOSITORY_ROOT = $stageRoot
if ([string]::IsNullOrWhiteSpace($env:LANDERP_TEST_ADMIN_CONNECTION)) {
    $env:LANDERP_TEST_ADMIN_CONNECTION = [Environment]::GetEnvironmentVariable('LANDERP_TEST_ADMIN_CONNECTION','User')
}
& $DotnetPath run --project src/LandErp.LocalSetup -c Release
if ($LASTEXITCODE -ne 0) { throw 'Local setup failed; credentials were not printed' }
$stageSecretDirectory = Join-Path $stageRoot 'local-data/stage1'
if ($IsWindows) {
    $stageDirectoryInfo = [IO.DirectoryInfo]::new($stageSecretDirectory)
    $stageAcl = [IO.FileSystemAclExtensions]::GetAccessControl($stageDirectoryInfo, [Security.AccessControl.AccessControlSections]::Access)
    $stageAcl.SetAccessRuleProtection($true,$false)
    $stageIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $stageRule = [Security.AccessControl.FileSystemAccessRule]::new($stageIdentity,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
    $stageAcl.SetAccessRule($stageRule)
    [IO.FileSystemAclExtensions]::SetAccessControl($stageDirectoryInfo, $stageAcl)
}
Write-Output 'Local initialized. Open local-data/stage1/owner-access.txt privately; MFA enrollment is required at first Owner login.'
