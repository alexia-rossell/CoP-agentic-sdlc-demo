FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Demo.slnx --locked-mode \
    && dotnet publish agent/Demo.Agent -c Release --no-restore -o /out

# The protected acceptance runner compiles candidates at runtime, so an SDK is required.
FROM mcr.microsoft.com/dotnet/sdk:10.0.402
WORKDIR /app
COPY --from=build /out ./host/
COPY global.json Demo.slnx ./
COPY app ./app/
COPY demo/ticket.json ./demo/ticket.json
RUN mkdir .demo && chown -R app:app /app
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
USER app
EXPOSE 8088
ENTRYPOINT ["dotnet", "host/Demo.Agent.dll", "serve"]
