# ============================================================
# JOIN.Services.WebApi — Dockerfile (multi-stage, Alpine)
# ============================================================
# Build context is the repo root so COPY src/ resolves every layer
# the API references (Domain, Application.DTO, Application,
# Infrastructure, Persistence). .dockerignore keeps bin/obj/,
# tests/, .git/, etc. out of the context.
# ============================================================

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copy ONLY project files first so the restore layer is cached
# independently of source changes.
COPY src/1.Domain/JOIN.Domain.csproj                    src/1.Domain/
COPY src/2.Application.DTO/JOIN.Application.DTO.csproj  src/2.Application.DTO/
COPY src/2.Application/JOIN.Application.csproj          src/2.Application/
COPY src/3.Infrastructure/JOIN.Infrastructure.csproj    src/3.Infrastructure/
COPY src/3.Persistence/JOIN.Persistence.csproj          src/3.Persistence/
COPY src/4.Services.WebApi/JOIN.Services.WebApi.csproj  src/4.Services.WebApi/

RUN dotnet restore src/4.Services.WebApi/JOIN.Services.WebApi.csproj

# Now copy the rest of the source and publish.
COPY src/ src/
RUN dotnet publish src/4.Services.WebApi/JOIN.Services.WebApi.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

# ---- final ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
# Install ICU and disable Globalization Invariant Mode so SqlClient can parse
# culture-aware connection strings. The base aspnet:alpine image sets
# DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1; icu-libs alone is not enough.
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app
COPY --from=build /app/publish .

# 8080 is the default HTTP port for non-root aspnet images (.NET 8+).
EXPOSE 8080

ENTRYPOINT ["dotnet", "JOIN.Services.WebApi.dll"]