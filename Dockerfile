# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:6.0
WORKDIR /app
COPY ./KoekoeBot/bin/Debug/netcoreapp3.1 .
ENTRYPOINT ["dotnet", "KoekoeBot.dll"]

# api is running on port 5137
EXPOSE 3941
CMD ["node", "index.js"]