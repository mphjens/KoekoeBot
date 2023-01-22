using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Exceptions;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.VoiceNext;
using DSPlus.Examples;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using SimpleWebSocketServerLibrary;

namespace KoekoeBot
{

    public class SavedGuildData
    {
        public string guildName;
        public ulong[] channelIds;
        public AlarmData[] alarms;
        public List<SampleData> samples;
    }

    class KoekoeController
    {
        public static readonly EventId BotEventId = new EventId(42, "KoekoeBot"); //??   
        public static DiscordClient Client { get; set; }
        public static CommandsNextExtension Commands { get; set; }
        public static VoiceNextExtension Voice { get; set; }

        //Guild id -> GuildHandler
        static Dictionary<ulong, GuildHandler> _instances;     

        public static async Task RunBot()
        {
            _instances = new Dictionary<ulong, GuildHandler>();

            string data_path = Path.Combine(Environment.CurrentDirectory, "volume", "data");
            if (!Directory.Exists(data_path))
            {
                Directory.CreateDirectory(data_path);
            }

            var json = "";
            using (var fs = File.OpenRead("volume/config.json"))
            using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                json = await sr.ReadToEndAsync();

            var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
            var cfg = new DiscordConfiguration
            {
                Token = cfgjson.Token,
                TokenType = TokenType.Bot,
                AutoReconnect = true,
                MinimumLogLevel = LogLevel.Debug,
            };

            Client = new DiscordClient(cfg);

            Client.Ready += Client_Ready;
            Client.GuildAvailable += Client_GuildAvailable;
            Client.GuildDeleted += Client_GuildDeleted;
            Client.ClientErrored += Client_ClientError;

            var ccfg = new CommandsNextConfiguration
            {
                // let's use the string prefix defined in config.json
                StringPrefixes = new[] { cfgjson.CommandPrefix },

                // enable responding in direct messages
                EnableDms = true,

                // enable mentioning the bot as a command prefix
                EnableMentionPrefix = true,
            };
            Commands = Client.UseCommandsNext(ccfg);

            Commands.CommandExecuted += Commands_CommandExecuted;
            Commands.CommandErrored += Commands_CommandErrored;

            Commands.RegisterCommands<KoekoeCommands>();

            Voice = Client.UseVoiceNext();

            await Client.ConnectAsync();

            await Task.Delay(-1); //Prevent premature quitting, TODO: find a nice way to gracefully exit
        }

        private static Task Client_Ready(DiscordClient sender, ReadyEventArgs e)
        {
            // let's log the fact that this event occured
            sender.Logger.LogInformation(BotEventId, "Client is ready to process events.");

            // since this method is not async, let's return
            // a completed task, so that no additional work
            // is done
            return Task.CompletedTask;
        }


        private static Task Client_ClientError(DiscordClient sender, ClientErrorEventArgs e)
        {
            // let's log the details of the error that just 
            // occured in our client
            sender.Logger.LogError(BotEventId, e.Exception, "Exception occured");

            // since this method is not async, let's return
            // a completed task, so that no additional work
            // is done
            return Task.CompletedTask;
        }

        private static Task Commands_CommandExecuted(CommandsNextExtension sender, CommandExecutionEventArgs e)
        {
            // let's log the name of the command and user
            e.Context.Client.Logger.LogInformation(BotEventId, $"{e.Context.User.Username} successfully executed '{e.Command.QualifiedName}'");

            // since this method is not async, let's return
            // a completed task, so that no additional work
            // is done
            return Task.CompletedTask;
        }

        private static async Task Commands_CommandErrored(CommandsNextExtension sender, CommandErrorEventArgs e)
        {
            // let's log the error details
            e.Context.Client.Logger.LogError(BotEventId, $"{e.Context.User.Username} tried executing '{e.Command?.QualifiedName ?? "<unknown command>"}' but it errored: {e.Exception.GetType()}: {e.Exception.Message ?? "<no message>"}", DateTime.Now);

            // let's check if the error is a result of lack
            // of required permissions
            if (e.Exception is ChecksFailedException ex)
            {
                // yes, the user lacks required permissions, 
                // let them know

                var emoji = DiscordEmoji.FromName(e.Context.Client, ":no_entry:");

                // let's wrap the response into an embed
                var embed = new DiscordEmbedBuilder
                {
                    Title = "Access denied",
                    Description = $"{emoji} You do not have the permissions required to execute this command.",
                    Color = new DiscordColor(0xFF0000) // red
                };
                await e.Context.RespondAsync(embed);
            }
        }
        private static Task Client_GuildAvailable(DiscordClient sender, GuildCreateEventArgs e)
        {
            if(KoekoeController._instances[e.Guild.Id].IsRunning) {
                KoekoeController._instances[e.Guild.Id].Stop();
                KoekoeController._instances.Remove(e.Guild.Id);
            }
                

            KoekoeController.StartupGuildHandler(sender, e).ContinueWith(async (task) => {
                // Console.WriteLine($"Guildhandler for {e.Guild.Name} stopped, trying to restart it automatically in 30 seconds.");
                // _instances.Remove(e.Guild.Id);

                // await Task.Delay(30 * 1000);
                // Client_GuildAvailable(sender, e);
            });

            return Task.CompletedTask;
        }

         private static Task Client_GuildDeleted(DiscordClient sender, GuildDeleteEventArgs e)
        {
            Console.WriteLine($"{e.Guild.Name} removed, stopping guildhandler");
            if(KoekoeController._instances[e.Guild.Id].IsRunning) {
                KoekoeController._instances[e.Guild.Id].Stop();
                KoekoeController._instances.Remove(e.Guild.Id);
            }
                
            return Task.CompletedTask;
        }

        public static async Task<bool> HandleWebsocketCommand(KoekoeWebsocketCommand cmd, SimpleWebSocketServer wsServer, WebSocketEventArg wsEvent)
        {
            GuildHandler handler = null;
            List<DiscordChannel> channels = null;
            Console.WriteLine($"guilds: '{String.Join(',',_instances.Keys)}'");
            if (cmd.type != KoekoeWebsocketCommand.WebsocketCommandType.GetGuilds && !_instances.TryGetValue(cmd.GuildId, out handler))
            {
                Console.WriteLine($"got '{cmd.type}' command for unknown guildid {cmd.GuildId}");
                return false;
            }

            if(cmd.channelIds != null) {
                Console.WriteLine($"getting channels");
                channels = await handler.GetChannels(cmd.channelIds?.ToList());
            }

            Console.WriteLine($"executing '{cmd.type}' command from {wsEvent.clientBaseUrl}");
            switch(cmd.type)
            {
                case KoekoeWebsocketCommand.WebsocketCommandType.PlayFile:
                    await handler.AnnounceFile(cmd.args[0], 1, channels);
                    break;
                case KoekoeWebsocketCommand.WebsocketCommandType.PlaySample:
                    await handler.AnnounceSample(cmd.args[0], 1, channels);
                    break;
                case KoekoeWebsocketCommand.WebsocketCommandType.Debug:

                    break;
                case KoekoeWebsocketCommand.WebsocketCommandType.GetGuilds:
                    string payload = JsonConvert.SerializeObject(getGuilds());
                    wsServer.SendTextMessage(payload, wsEvent.clientId);
                    Console.WriteLine($"sent {payload} over websocket");
                    break;
                case KoekoeWebsocketCommand.WebsocketCommandType.GetChannels:
                    wsServer.SendTextMessage(JsonConvert.SerializeObject(await getChannels(cmd.GuildId)), wsEvent.clientId);
                    break;

                case KoekoeWebsocketCommand.WebsocketCommandType.GetSamples:
                    wsServer.SendTextMessage(JsonConvert.SerializeObject(getSamples(cmd.GuildId)), wsEvent.clientId);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(cmd.type), cmd.type, "Unknown websocket command type");
            }

            return true;
        }

        public static KoekoeDiscordIdList getGuilds()
        {
            var retval = new KoekoeDiscordIdList();
            retval.type = KoekoeDiscordIdList.KoekoeIdListType.Guilds;
            retval.items = _instances.Values.Select(x => x.getDiscordId()).ToArray();
            return retval;
        }

        public static async Task<KoekoeDiscordIdList> getChannels(ulong guildid, bool noCache = false)
        {
            var retval = new KoekoeDiscordIdList();
            retval.type = KoekoeDiscordIdList.KoekoeIdListType.Channels;
            if(noCache) {
                retval.items = (await _instances[guildid].GetChannels(_instances[guildid].ChannelIds)).Select(x=>new KoekoeDiscordId { Id = x.Id.ToString(), Name = x.Name }).ToArray();
            } else {
                 retval.items = (await _instances[guildid].GetChannelsCached(_instances[guildid].ChannelIds)).Select(x=>new KoekoeDiscordId { Id = x.Id.ToString(), Name = x.Name }).ToArray();
            }
            
            return retval;
        }

        public static KoekoeDiscordIdList getSamples(ulong guildid)
        {
            var retval = new KoekoeDiscordIdList();
            retval.type = KoekoeDiscordIdList.KoekoeIdListType.Samples;
            retval.items = _instances[guildid].GetGuildData().samples.Select((x, i) => new KoekoeDiscordId { Id = x.SampleAliases[0], Name = x.Name }).ToArray();
            return retval;
        }

        //Hooked up in program.cs
        public static Task StartupGuildHandler(DiscordClient sender, GuildCreateEventArgs e)
        {
            // let's log the name of the guild that was just
            // sent to our client
            //sender.Logger.LogInformation(Program.BotEventId, $"Guild available: {e.Guild.Name}");

            Console.WriteLine($"Starting guild handler for {e.Guild.Name}");


            //Create or get the handler for this guild
            GuildHandler handler = KoekoeController.GetGuildHandler(sender, e.Guild);

            //Read our saved data
            string guilddata_path = Path.Combine(Environment.CurrentDirectory, "volume", "data", $"guilddata_{e.Guild.Id}.json");
            if (File.Exists(guilddata_path))
            {
                string json = File.ReadAllText(guilddata_path).ToString();
                var dataObj = JsonConvert.DeserializeObject<SavedGuildData>(json);

                handler.SetGuildData(dataObj);

                //Restore registered channels
                
                if (dataObj.channelIds != null)
                {
                    handler.ChannelIds.Clear();
                    handler.ChannelIds.AddRange(dataObj.channelIds);
                }

                //Restore alarms
                if (dataObj.alarms != null)
                {
                    foreach (AlarmData alarm in dataObj.alarms)
                    {
                        if (alarm != null)
                            handler.AddAlarm(alarm.AlarmDate, alarm.AlarmName, alarm.sampleid, alarm.userId, false);
                    }
                }
            }

            handler.GetGuildData().guildName = e.Guild.Name; // todo: find a better place
            handler.UpdateSamplelist();

            if (!handler.IsRunning) //Run the handler loop if it's not already started
                return handler.Execute();
            
            return Task.CompletedTask;

        }

        public static GuildHandler GetGuildHandler(DiscordClient client, DiscordGuild guild, bool create=true)
        {
            string sampleDirectory = Path.Combine("volume","samples", guild.Id.ToString());
            if(!Directory.Exists(sampleDirectory))
            {
                Directory.CreateDirectory(sampleDirectory);
            }

            if(_instances.ContainsKey(guild.Id))
            {
                return _instances[guild.Id];
            }

            if (create)
            {
                GuildHandler nHandler = new GuildHandler(client, guild);
                _instances.Add(guild.Id, nHandler);
                nHandler.SetGuildData(new SavedGuildData());
                return nHandler;
            }

            return null;
        }
                
    }
}
