FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["SkypointShopifyPlugin.sln", "."]
COPY ["SkypointShopifyPlugin.WebUI/SkypointShopifyPlugin.WebUI.csproj", "SkypointShopifyPlugin.WebUI/"]
COPY ["SkypointShopifyPlugin.Core/SkypointShopifyPlugin.Core.csproj", "SkypointShopifyPlugin.Core/"]
RUN dotnet restore "SkypointShopifyPlugin.WebUI/SkypointShopifyPlugin.WebUI.csproj"
COPY . .
WORKDIR "/src/SkypointShopifyPlugin.WebUI"
RUN dotnet build "SkypointShopifyPlugin.WebUI.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "SkypointShopifyPlugin.WebUI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SkypointShopifyPlugin.WebUI.dll"]
