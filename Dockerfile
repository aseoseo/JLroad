FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["JIroad.csproj", "./"]
RUN dotnet restore "JIroad.csproj"

COPY . .
RUN dotnet build "JIroad.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "JIroad.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "JIroad.dll"]
