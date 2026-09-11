# Mount a volume to {workdir}/volume
# place a config.json file in there, see this repo for an example
# samples (mp3) can be placed in volume/samples/{guild id}/

# Build runtime image
# DSharpPlus.Natives.Opus ships a libopus.so built against glibc 2.43 (it needs
# sqrtf@GLIBC_2.43), so it cannot load on the default aspnet:10.0 image, which is
# Ubuntu 24.04 with glibc 2.39. resolute is Ubuntu 26.04 with glibc 2.43.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-resolute
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