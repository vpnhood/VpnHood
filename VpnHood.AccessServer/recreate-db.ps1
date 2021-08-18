cd "$PSScriptRoot"
$migrationPath="$PSScriptRoot/Migrations";

if (Test-Path $migrationPath )
{
	Remove-Item "$migrationPath/*" -Recurse;
}

dotnet ef migrations add Init;
dotnet ef database drop -f;
dotnet ef database update --no-build;
