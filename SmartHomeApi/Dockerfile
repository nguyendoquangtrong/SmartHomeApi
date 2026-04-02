FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Đã sửa lại đường dẫn (bỏ tiền tố SmartHomeApi/)
COPY ["SmartHomeApi.csproj", "./"]
RUN dotnet restore "./SmartHomeApi.csproj"
COPY . .

# Xóa dòng chuyển Workdir vì hiện tại đã đứng đúng chỗ
RUN dotnet build "./SmartHomeApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SmartHomeApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SmartHomeApi.dll"]