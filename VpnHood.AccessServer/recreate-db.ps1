cd "$PSScriptRoot"
$migrationPath="$PSScriptRoot/Migrations";

# remove old migrations
if (Test-Path $migrationPath )
{
	echo "Removing migrations...";
	Remove-Item "$migrationPath/*" -Recurse;
}


echo "Initializing the Migrations ...";
dotnet ef migrations add Init;
dotnet ef database drop -f;

echo "Updating the database ...";
dotnet ef database update --no-build;

# remove migrations
if (Test-Path $migrationPath )
{
	echo "Removing migrations...";
	Remove-Item "$migrationPath/*" -Recurse;
}
dotnet build

