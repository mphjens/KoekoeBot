# Mount a volume to {workdir}/volume
# place a config.json file in there, see this repo for an example
# samples (mp3) can be placed in volume/samples/{guild id}/

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
ENV TZ=Europe/Amsterdam

# DSharpPlus.Voice checks for OpenSSL 3.x at startup via a plain "libcrypto"
# P/Invoke. libcrypto.so.3 is already present (the aspnet image depends on
# libssl3 for .NET's own crypto), just missing the unversioned symlink
# (normally provided by libssl-dev, which we don't want to install here).
RUN ln -sf "$(find /usr/lib -name 'libcrypto.so.3' | head -n1)" /usr/lib/libcrypto.so

WORKDIR /app
# Our ci pipeline places the build in the ./dist directory
COPY ./dist .

ENTRYPOINT ["dotnet", "KoekoeBot.dll"]

# websocket server is running on port 3941
EXPOSE 3941
CMD ["node", "index.js"]