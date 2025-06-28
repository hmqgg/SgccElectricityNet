FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/SgccElectricityNet.Worker/SgccElectricityNet.Worker.csproj", "src/SgccElectricityNet.Worker/"]
RUN dotnet restore "src/SgccElectricityNet.Worker/SgccElectricityNet.Worker.csproj"
COPY src/. .
WORKDIR "/src/SgccElectricityNet.Worker"
RUN dotnet build "./SgccElectricityNet.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SgccElectricityNet.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=build /usr/share/powershell /usr/share/powershell
ENV PLAYWRIGHT_BROWSERS_PATH=/app/.playwright
USER root
RUN \
    ln -s /usr/share/powershell/pwsh /usr/bin/pwsh && \
    pwsh /app/playwright.ps1 install --with-deps chromium && \
    rm -rf /usr/share/powershell /usr/bin/pwsh && \
    chmod -R +x /app/.playwright && \
    chown -R $APP_UID:$APP_UID /app/.playwright
USER $APP_UID
ENTRYPOINT ["dotnet", "SgccElectricityNet.Worker.dll"]
