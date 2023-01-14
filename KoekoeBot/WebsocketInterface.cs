using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SimpleWebSocketServerLibrary;

namespace KoekoeBot
{

    public class KoekoeWebsocketCommand
    {
        public enum WebsocketCommandType
        {
            GetGuilds,
            GetChannels,
            PlaySample,
            PlayFile,
            Debug,
            GetSamples,
        }

        public ulong GuildId;
        public ulong[] channelIds;
        public WebsocketCommandType type;
        public string[] args;

    }

    public class KoekoeDiscordIdList
    {
        public enum KoekoeIdListType
        {
            Guilds,
            Channels,
            Samples
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public KoekoeIdListType type;
        public KoekoeDiscordId[] items;
    }

    public class KoekoeDiscordId
    {
        public string Id;
        public string Name;
    }

    internal class WebsocketInterface
    {
        bool serving = false;
        ConcurrentQueue<KoekoeWebsocketCommand> queue;
        SimpleWebSocketServer websocketServer;

        public WebsocketInterface(ref ConcurrentQueue<KoekoeWebsocketCommand> queue)
        {
            this.queue = queue;
        }

        public async Task<bool> Serve()
        {
            serving = true;
            websocketServer = new SimpleWebSocketServer(new SimpleWebSocketServerSettings { port = 3941});
            websocketServer.WebsocketServerEvent += WebsocketServer_WebsocketServerEvent;

            websocketServer.StartServer();

            serving = false;

            return true;
        }

        private void WebsocketServer_WebsocketServerEvent(object sender, WebSocketEventArg args)
        {
            if (args.data != null && args.isText)
            {
                string received = Encoding.UTF8.GetString(args.data);
                // websocketServer.SendTextMessage("Client: " + args.clientId + " on url: " + args.clientBaseUrl + ", says: " + received);
                Console.WriteLine("Got ws message: " + received);
                try
                {
                    var cmd = JsonConvert.DeserializeObject<KoekoeWebsocketCommand>(received);
                    Console.WriteLine($"received '{cmd.type}' command from {sender.ToString()}");
                    KoekoeController.HandleWebsocketCommand(cmd, this.websocketServer, args);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error while executing command from websocket..");
                    Console.WriteLine(ex.Message);
                }

                //this.queue.Enqueue(new KoekoeWebsocketCommand() { type = KoekoeWebsocketCommand.WebsocketCommandType.PlaySample, data = received });
            }
        }
    }
}
