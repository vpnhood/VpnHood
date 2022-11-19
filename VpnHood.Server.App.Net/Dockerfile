#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["VpnHood.Server.App.Net/VpnHood.Server.App.Net.csproj", "VpnHood.Server.App.Net/"]
COPY ["VpnHood.Server/VpnHood.Server.csproj", "VpnHood.Server/"]
COPY ["VpnHood.Server.Access/VpnHood.Server.Access.csproj", "VpnHood.Server.Access/"]
COPY ["VpnHood.Common/VpnHood.Common.csproj", "VpnHood.Common/"]
COPY ["VpnHood.Tunneling/VpnHood.Tunneling.csproj", "VpnHood.Tunneling/"]
RUN dotnet restore "VpnHood.Server.App.Net/VpnHood.Server.App.Net.csproj"
COPY . .
WORKDIR "/src/VpnHood.Server.App.Net"
RUN dotnet build "VpnHood.Server.App.Net.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "VpnHood.Server.App.Net.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "VpnHoodServer.dll"]