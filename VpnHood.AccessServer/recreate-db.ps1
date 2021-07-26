$migrationPath="$PSScriptRoot/Migrations";
if (Test-Path $migrationPath )
{
	Remove-Item Migrations\* -Recurse;
}

dotnet ef database drop -f;
dotnet ef migrations add Init;
dotnet ef database update;
