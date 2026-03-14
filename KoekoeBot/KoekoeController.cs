using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.EventArgs;                              // CommandErroredEventArgs
using DSharpPlus.Commands.Exceptions;                            // ChecksFailedException
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.TextCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;                                       // SessionCreatedEventArgs etc.
using DSharpPlus.VoiceNext;
using DSPlus.Examples;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using SimpleWebSocketServerLibrary;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Enums;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.Commands.Processors.TextCommands.Parsing;
using DSharpPlus.Net.Gateway;

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
        public static readonly EventId BotEventId = new EventId(42, "KoekoeBot");
        public static DiscordClient Client { get; set; }
        public static CommandsExtension Commands { get; set; }

        static Dictionary<ulong, GuildHandler> _instances;

        public static async Task RunBot()
        {
            _instances = new Dictionary<ulong, GuildHandler>();

            string data_path = Path.Combine(Environment.CurrentDirectory, "volume", "data");
            if (!Directory.Exists(data_path))
                Directory.CreateDirectory(data_path);

            var json = "";
            using (var fs = File.OpenRead("volume/config.json"))
            using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                json = await sr.ReadToEndAsync();

            var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);

            DiscordClientBuilder builder = DiscordClientBuilder.CreateDefault(
                cfgjson.Token,
                DiscordIntents.Guilds | DiscordIntents.GuildMessages | DiscordIntents.GuildVoiceStates
            );

            builder.SetLogLevel(LogLevel.Debug);

            // Replaces AutoReconnect = true
            builder.ConfigureServices(services =>
                services.AddSingleton<IGatewayController, ReconnectingGatewayController>());

            // Replaces ClientErrored event — v5 uses IClientErrorHandler
            builder.ConfigureServices(services =>
                services.AddSingleton<IClientErrorHandler, KoekoeClientErrorHandler>());

            builder.ConfigureEventHandlers(b => b
                .HandleSessionCreated(Client_Ready)
                .HandleGuildAvailable(Client_GuildAvailable)
                .HandleGuildDeleted(Client_GuildDeleted)
            );

            builder.UseCommands((IServiceProvider sp, CommandsExtension ext) =>
            {
                Commands = ext; // capture reference here — no GetExtension() in v5

                TextCommandProcessor textProcessor = new(new TextCommandConfiguration
                {
                    // true = also respond to @mention as prefix
                    PrefixResolver = new DefaultPrefixResolver(true, cfgjson.CommandPrefix).ResolvePrefixAsync
                });

                ext.AddProcessor(textProcessor);
                ext.AddProcessor(new SlashCommandProcessor());

                ext.AddCommands([typeof(KoekoeCommands), typeof(KoekoeSlashCommands)]);

                // Delegate signature must be: Task(CommandsExtension, CommandErroredEventArgs)
                ext.CommandErrored += Commands_CommandErrored;
            });

            builder.UseInteractivity(new InteractivityConfiguration
            {
                PaginationBehaviour = PaginationBehaviour.WrapAround,
                Timeout = TimeSpan.FromMinutes(2),
            });

            // VoiceNext requires a config object in v5
            builder.UseVoiceNext(new VoiceNextConfiguration());
            // ConnectAsync on the builder builds + connects in one step
            Client = builder.Build();
            await Client.ConnectAsync();

            await Task.Delay(-1);
        }

        // SessionCreatedEventArgs — note: NOT SessionReadyEventArgs
        private static Task Client_Ready(DiscordClient sender, SessionCreatedEventArgs e)
        {
            sender.Logger.LogInformation(BotEventId, "Client is ready to process events.");
            return Task.CompletedTask;
        }

        // Signature must exactly match AsyncEventHandler<CommandsExtension, CommandErroredEventArgs>
        private static async Task Commands_CommandErrored(CommandsExtension sender, CommandErroredEventArgs e)
        {
            sender.Client.Logger.LogError(BotEventId,
                $"{e.Context.User.Username} tried executing '{e.Context.Command?.FullName ?? "<unknown>"}' " +
                $"but it errored: {e.Exception.GetType()}: {e.Exception.Message ?? "<no message>"}");

            if (e.Exception is ChecksFailedException)
            {
                var emoji = DiscordEmoji.FromName(sender.Client, ":no_entry:");
                var embed = new DiscordEmbedBuilder
                {
                    Title = "Access denied",
                    Description = $"{emoji} You do not have the permissions required to execute this command.",
                    Color = new DiscordColor(0xFF0000)
                };
                await e.Context.RespondAsync(embed);
            }
        }

        private static Task Client_GuildAvailable(DiscordClient sender, GuildAvailableEventArgs e)
        {
            if (_instances.ContainsKey(e.Guild.Id) && _instances[e.Guild.Id].IsRunning)
            {
                _instances[e.Guild.Id].Stop();
                _instances.Remove(e.Guild.Id);
            }
            KoekoeController.StartupGuildHandler(sender, e).ContinueWith(_ => { });
            return Task.CompletedTask;
        }

        private static Task Client_GuildDeleted(DiscordClient sender, GuildDeletedEventArgs e)
        {
            Client.Logger.LogInformation($"{e.Guild.Name} removed, stopping guildhandler");
            if (_instances.ContainsKey(e.Guild.Id) && _instances[e.Guild.Id].IsRunning)
            {
                _instances[e.Guild.Id].Stop();
                _instances.Remove(e.Guild.Id);
            }
            return Task.CompletedTask;
        }

        public static async Task<bool> HandleWebsocketCommand(KoekoeWebsocketCommand cmd, SimpleWebSocketServer wsServer, WebSocketEventArg wsEvent)
        {
            GuildHandler handler = null;
            List<DiscordChannel> channels = null;
            if (cmd.type != KoekoeWebsocketCommand.WebsocketCommandType.GetGuilds && !_instances.TryGetValue(cmd.GuildId, out handler))
            {
                Client.Logger.LogWarning($"got '{cmd.type}' command for unknown guildid {cmd.GuildId}");
                return false;
            }

            if (cmd.channelIds != null)
                channels = await handler.GetChannels(cmd.channelIds?.ToList());

            Client.Logger.LogInformation($"executing '{cmd.type}' command from {wsEvent.clientBaseUrl}");
            switch (cmd.type)
            {
                case KoekoeWebsocketCommand.WebsocketCommandType.PlayFile:
                    handler.AnnounceFile(cmd.args[0], 1, channels); break;
                case KoekoeWebsocketCommand.WebsocketCommandType.PlaySample:
                    handler.AnnounceSample(cmd.args[0], 1, channels); break;
                case KoekoeWebsocketCommand.WebsocketCommandType.Debug:
                    break;
                case KoekoeWebsocketCommand.WebsocketCommandType.GetGuilds:
                    wsServer.SendTextMessage(JsonConvert.SerializeObject(getGuilds()), wsEvent.clientId); break;
                case KoekoeWebsocketCommand.WebsocketCommandType.GetChannels:
                    wsServer.SendTextMessage(JsonConvert.SerializeObject(await getChannels(cmd.GuildId)), wsEvent.clientId); break;
                case KoekoeWebsocketCommand.WebsocketCommandType.GetSamples:
                    wsServer.SendTextMessage(JsonConvert.SerializeObject(getSamples(cmd.GuildId)), wsEvent.clientId); break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(cmd.type), cmd.type, "Unknown websocket command type");
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
            if (noCache)
                retval.items = (await _instances[guildid].GetChannels(_instances[guildid].ChannelIds))
                    .Select(x => new KoekoeDiscordId { Id = x.Id.ToString(), Name = x.Name }).ToArray();
            else
                retval.items = (await _instances[guildid].GetChannelsCached(_instances[guildid].ChannelIds))
                    .Select(x => new KoekoeDiscordId { Id = x.Id.ToString(), Name = x.Name }).ToArray();
            return retval;
        }

        public static KoekoeDiscordIdList getSamples(ulong guildid)
        {
            var retval = new KoekoeDiscordIdList();
            retval.type = KoekoeDiscordIdList.KoekoeIdListType.Samples;
            retval.items = _instances[guildid].GetGuildData().samples
                .Select((x, i) => new KoekoeDiscordId { Id = x.SampleAliases[0], Name = x.Name }).ToArray();
            return retval;
        }

        public static Task StartupGuildHandler(DiscordClient sender, GuildAvailableEventArgs e)
        {
            Client.Logger.LogInformation($"Starting guild handler for {e.Guild.Name}");

            GuildHandler handler = KoekoeController.GetGuildHandler(sender, e.Guild);

            string guilddata_path = Path.Combine(Environment.CurrentDirectory, "volume", "data", $"guilddata_{e.Guild.Id}.json");
            if (File.Exists(guilddata_path))
            {
                string savedJson = File.ReadAllText(guilddata_path);
                var dataObj = JsonConvert.DeserializeObject<SavedGuildData>(savedJson);
                handler.SetGuildData(dataObj);

                if (dataObj.channelIds != null)
                {
                    handler.ChannelIds.Clear();
                    handler.ChannelIds.AddRange(dataObj.channelIds);
                }

                if (dataObj.alarms != null)
                {
                    foreach (AlarmData alarm in dataObj.alarms)
                    {
                        if (alarm != null)
                            handler.AddAlarm(alarm.AlarmDate, alarm.AlarmName, alarm.sampleid, alarm.userId, false);
                    }
                }
            }

            handler.GetGuildData().guildName = e.Guild.Name;
            handler.UpdateSamplelist();

            if (!handler.IsRunning)
            {
                // Braces required here — CS1023: var declaration cannot be a bare embedded statement
                var _ = Task.Factory.StartNew(async () => { await handler.Execute(); }, TaskCreationOptions.LongRunning);
            }

            return Task.CompletedTask;
        }

        public static GuildHandler GetGuildHandler(DiscordClient client, DiscordGuild guild, bool create = true)
        {
            string sampleDirectory = Path.Combine("volume", "samples", guild.Id.ToString());
            if (!Directory.Exists(sampleDirectory))
                Directory.CreateDirectory(sampleDirectory);

            if (_instances.ContainsKey(guild.Id))
                return _instances[guild.Id];

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