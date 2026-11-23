FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /app
COPY Directory.Build.props ./
COPY src ./src
RUN dotnet publish src/$PROJECT/$PROJECT.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG PROJECT
ENV PROJECT=$PROJECT
WORKDIR /app
COPY --from=build /out .
RUN mkdir /data && chown $APP_UID /data
USER $APP_UID
ENTRYPOINT ["sh", "-c", "exec dotnet $PROJECT.dll"]
