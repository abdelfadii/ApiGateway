FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# force clean nuget behavior (IMPORTANT FIX)
ENV NUGET_FALLBACK_PACKAGES=""

COPY . .

RUN dotnet restore --force-evaluate
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 80
ENV ASPNETCORE_URLS=http://+:80

ENTRYPOINT ["dotnet", "SirmarocGateway.dll"]