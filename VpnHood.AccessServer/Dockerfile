#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["VpnHood.AccessServer/VpnHood.AccessServer.csproj", "VpnHood.AccessServer/"]
RUN dotnet restore "VpnHood.AccessServer/VpnHood.AccessServer.csproj"
COPY . .
WORKDIR "/src/VpnHood.AccessServer"
RUN dotnet build "VpnHood.AccessServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "VpnHood.AccessServer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "VpnHood.AccessServer.dll"]