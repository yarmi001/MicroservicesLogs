FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Common/Common.csproj", "Common/"]
COPY ["Producer/Producer.csproj", "Producer/"]

RUN dotnet restore "Producer/Producer.csproj"

COPY . .

WORKDIR "/src/Producer"
RUN dotnet publish "Producer.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Producer.dll"]