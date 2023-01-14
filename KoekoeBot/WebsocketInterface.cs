using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using WatsonWebsocket;

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
        WatsonWsServer websocketServer;

        public WebsocketInterface(ref ConcurrentQueue<KoekoeWebsocketCommand> queue)
        {
            this.queue = queue;
        }

        public async Task<bool> Serve()
        {
            serving = true;
            websocketServer = new WatsonWsServer("0.0.0.0", 3941, true);
            //websocketServer.ClientConnected += ClientConnected;
            //websocketServer.ClientDisconnected += ClientDisconnected;
            websocketServer.MessageReceived += MessageReceived; 

            websocketServer.Start();

            serving = false;

            return true;
        }

        private void MessageReceived(object sender, MessageReceivedEventArgs args) {
            if (args.Data != null)
            {
                string received = Encoding.UTF8.GetString(args.Data);
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
