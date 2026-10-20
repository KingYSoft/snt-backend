# path
$packFolder = (Get-Item -Path "./" -Verbose).FullName
$rootFolder = Join-Path $packFolder "/"

# List of projects
$projects = ( 
	"/src/SntBackend.Web.Host"
)


# Rebuild all solutions
$solutionFolder = $rootFolder 
Set-Location $solutionFolder 
	#& dotnet build

# publish 
foreach($project in $projects){
	$projectFolder = Join-Path $solutionFolder $project
	Set-Location $projectFolder
		#& dotnet build

	$releasePath = Join-Path $projectFolder "bin/Release" 
	if((Test-Path $releasePath)){
		Remove-Item -Recurse $releasePath -Force
	}
	
  dotnet publish -c Release -r win-x64 --no-self-contained

	if (-Not $?) {
        Write-Host ("Packaging failed for the project: " + $projectFolder)
        #exit $LASTEXITCODE
    }
}
Set-Location $solutionFolder