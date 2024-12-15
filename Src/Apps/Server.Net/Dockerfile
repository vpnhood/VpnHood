#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["Src/Apps/VpnHood.Core.Server.App.Net/NLog.xsd", "VpnHood.Core.Server.App.Net/"]
COPY ["Src/Apps/VpnHood.Core.Server.App.Net/NLog.config", "VpnHood.Core.Server.App.Net/"]
COPY ["Src/Apps/VpnHood.Core.Server.App.Net/VpnHood.Core.Server.App.Net.csproj", "VpnHood.Core.Server.App.Net/"]
COPY ["Core/VpnHood.Core.Server/VpnHood.Core.Server.csproj", "VpnHood.Core.Server/"]
COPY ["Core/VpnHood.Core.Common/VpnHood.Core.Common.csproj", "VpnHood.Core.Common/"]
COPY ["Core/VpnHood.Core.Tunneling/VpnHood.Core.Tunneling.csproj", "VpnHood.Core.Tunneling/"]
COPY ["Core/VpnHood.Core.Server.Access/VpnHood.Core.Server.Access.csproj", "VpnHood.Core.Server.Access/"]
RUN dotnet restore "VpnHood.Core.Server.App.Net/VpnHood.Core.Server.App.Net.csproj"
COPY . .
WORKDIR "/src/Apps/VpnHood.Core.Server.App.Net"
RUN dotnet build "VpnHood.Core.Server.App.Net.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "VpnHood.Core.Server.App.Net.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "VpnHoodServer.dll"]