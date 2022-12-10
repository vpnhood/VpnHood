cd "$PSScriptRoot"

dotnet build
dotnet run /recreatedb /initOnly

