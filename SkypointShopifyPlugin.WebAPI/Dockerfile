FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["SkypointShopifyPlugin.sln", "."]
COPY ["SkypointShopifyPlugin.WebAPI/SkypointShopifyPlugin.WebAPI.csproj", "SkypointShopifyPlugin.WebAPI/"]
COPY ["SkypointShopifyPlugin.Core/SkypointShopifyPlugin.Core.csproj", "SkypointShopifyPlugin.Core/"]
COPY ["SkypointShopifyPlugin.Application/SkypointShopifyPlugin.Application.csproj", "SkypointShopifyPlugin.Application/"]
COPY ["SkypointShopifyPlugin.Infrastructure/SkypointShopifyPlugin.Infrastructure.csproj", "SkypointShopifyPlugin.Infrastructure/"]
RUN dotnet restore "SkypointShopifyPlugin.WebAPI/SkypointShopifyPlugin.WebAPI.csproj"
COPY . .
WORKDIR "/src/SkypointShopifyPlugin.WebAPI"
RUN dotnet build "SkypointShopifyPlugin.WebAPI.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "SkypointShopifyPlugin.WebAPI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SkypointShopifyPlugin.WebAPI.dll"]
