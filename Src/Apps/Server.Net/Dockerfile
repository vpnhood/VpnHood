#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["Src/Apps/Server.Net/NLog.xsd", "VpnHood.App.Server.App.Net/"]
COPY ["Src/Apps/Server.Net/NLog.config", "VpnHood.App.Server.Net/"]
COPY ["Src/Apps/Server.Net/VpnHood.App.Server.Net.csproj", "VpnHood.App.Server.Net/"]
COPY ["Src/Core/VpnHood.Core.Toolkit/VpnHood.Core.Toolkit.csproj", "VpnHood.Core.Toolkit/"]
COPY ["Src/Core/VpnHood.Core.VpnAdapters.Abstractions/VpnHood.Core.VpnAdapters.Abstractions.csproj", "VpnHood.Core.VpnAdapters.Abstractions/"]
COPY ["Src/Core/VpnHood.Core.VpnAdapters.LinuxTun/VpnHood.Core.VpnAdapters.LinuxTun.csproj", "VpnHood.Core.VpnAdapters.LinuxTun/"]
COPY ["Src/Core/VpnHood.Core.Common/VpnHood.Core.Common.csproj", "VpnHood.Core.Common/"]
COPY ["Src/Core/VpnHood.Core.Tunneling/VpnHood.Core.Tunneling.csproj", "VpnHood.Core.Tunneling/"]
COPY ["Src/Core/VpnHood.Core.Server/VpnHood.Core.Server.csproj", "VpnHood.Core.Server/"]
COPY ["Src/Core/VpnHood.Core.Server.Access/VpnHood.Core.Server.Access.csproj", "VpnHood.Core.Server.Access/"]
RUN dotnet restore "VpnHood.App.Server.Net/VpnHood.App.Server.Net.csproj"
COPY . .
WORKDIR "/src/Src/Apps/Server.Net"
RUN dotnet build "VpnHood.App.Server.Net.csproj" -c Release -p:SolutionDir=.  -o /app/build

FROM build AS publish
RUN dotnet publish "VpnHood.App.Server.Net.csproj" -c Release -p:SolutionDir=. -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "VpnHoodServer.dll"]