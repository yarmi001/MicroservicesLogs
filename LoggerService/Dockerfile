FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Common/Common.csproj", "Common/"]
COPY ["LoggerService/LoggerService.csproj", "LoggerService/"]

RUN dotnet restore "LoggerService/LoggerService.csproj"

COPY . .

WORKDIR "/src/LoggerService"
RUN dotnet publish "LoggerService.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LoggerService.dll"]