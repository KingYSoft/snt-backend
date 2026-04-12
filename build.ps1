param(
  [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

dotnet build SntBackend.sln -c $Configuration -m:1 -p:RestoreUseStaticGraphEvaluation=true
