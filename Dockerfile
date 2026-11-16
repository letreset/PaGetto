ARG Version=1.0.0

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG Version
ARG TARGETARCH
# package source for `dotnet restore`; override it to build without access to nuget.org
ARG NuGetSource=https://api.nuget.org/v3/index.json
WORKDIR /src

## Create separate layer for `dotnet restore` to allow for caching; useful for local development
# copy only necessary files
COPY ./Directory.Packages.props ./nuget.config ./src/**/*.csproj ./
# create folder structure that was lost when copying
RUN for file in $(ls *.csproj); do mkdir -p ${file%.*}/ && mv $file ${file%.*}/; done
# useful for debugging to display all files
#RUN echo $(ls)
# restore packages
RUN dotnet restore PaGetto/PaGetto.csproj --arch $TARGETARCH --source "$NuGetSource"

## Publish app (implicitly builds the app)
FROM build AS publish
ARG Version
# copy all files
COPY /src .
RUN dotnet publish PaGetto \
    --configuration Release \
    --output /app \
    --no-restore \
    -p Version=${Version} \
    -p DebugType=none \
    -p DebugSymbols=false \
    -p GenerateDocumentationFile=false \
    -p UseAppHost=false \
    -a $TARGETARCH

# create default folders
RUN mkdir -p "/data/packages" "/data/symbols" "/data/db"

## Create final image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
# install cultures (same approach as Alpine SDK image)
RUN apk add --no-cache icu-libs icu-data-full tzdata
# disable the invariant mode (set in base image)
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
# set default configurations; use the `/data` folder for packages, symbols and the SQLite database
ENV Storage__Path "/data"
ENV Search__Type "Database"
ENV Database__Type "Sqlite"
ENV Database__ConnectionString "Data Source=/data/db/pagetto.db"
LABEL org.opencontainers.image.source="https://github.com/letreset/PaGetto"
# copy default folders
COPY --from=publish /data /data
# copy the published app
WORKDIR /app
COPY --from=publish /app .

ENTRYPOINT ["dotnet", "PaGetto.dll"]
