FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Common/Common.csproj", "Common/"]
COPY ["Consumer/Consumer.csproj", "Consumer/"]

RUN dotnet restore "Consumer/Consumer.csproj"

COPY . .

WORKDIR "/src/Consumer"
RUN dotnet publish "Consumer.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Consumer.dll"]