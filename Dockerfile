# Mount a volume to {workdir}/volume
# place a config.json file in there, see this repo for an example
# samples (mp3) can be placed in volume/samples/{guild id}/

# Build runtime image
FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-bionic
WORKDIR /app
# Our ci pipeline places the build in the ./out directory
COPY ./dist .

# we need ffmpeg to play the audio samples
RUN apt-get update
RUN apt-get install ffmpeg -y
ENTRYPOINT ["dotnet", "KoekoeBot.dll"]

# websocket server is running on port 3941
EXPOSE 3941
CMD ["node", "index.js"]