using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSPlus.Examples;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace KoekoeBot
{

    class SavedGuildData
    {
        public ulong[] channelIds;
        public AlarmData[] alarms;
    }

    class KoekoeController
    {
        //Guild id -> ChannelHandler
        static Dictionary<ulong, GuildHandler> _instances;
        

        public static void Initialize()
        {
            _instances = new Dictionary<ulong, GuildHandler>();

            string data_path = Path.Combine(Environment.CurrentDirectory, "data");
            if (!Directory.Exists(data_path))
            {
                Directory.CreateDirectory(data_path);
            }    

        }

        //Hooked up in program.cs
        public static Task Client_GuildAvailable(DiscordClient sender, GuildCreateEventArgs e)
        {
            // let's log the name of the guild that was just
            // sent to our client
            sender.Logger.LogInformation(Program.BotEventId, $"Guild available: {e.Guild.Name}");

            //Create or get the handler for this guild
            GuildHandler handler = KoekoeController.GetGuildHandler(sender, e.Guild);

            //Read our saved data
            string guilddata_path = Path.Combine(Environment.CurrentDirectory, "data", $"guilddata_{e.Guild.Id}.json");
            if (File.Exists(guilddata_path))
            {
                string json = File.ReadAllText(guilddata_path).ToString();
                var dataObj = JsonConvert.DeserializeObject< SavedGuildData>(json);
                //Restore registered channels
                if (dataObj.channelIds != null)
                {
                    foreach (ulong channelid in dataObj.channelIds)
                    {
                        DiscordChannel channel = e.Guild.GetChannel(channelid);
                        
                        if(channel != null)
                            handler.AddChannel(channel, false);
                    }
                }

                //Restore alarms
                if (dataObj.alarms != null)
                {
                    foreach (AlarmData alarm in dataObj.alarms)
                    {
                        if (alarm != null)
                            handler.AddAlarm(alarm.AlarmDate, alarm.AlarmName, alarm.userId, false);
                    }
                }

            }

            if (!handler.IsRunning) //Run the handler loop if it's not already started
                handler.Execute(); //Will run async


            // since this method is not async, let's return
            // a completed task, so that no additional work
            // is done
            return Task.CompletedTask;
        }

        public static GuildHandler GetGuildHandler(DiscordClient client, DiscordGuild guild, bool create=true)
        {
            if(_instances.ContainsKey(guild.Id))
            {
                return _instances[guild.Id];
            }

            if (create)
            {
                GuildHandler nHandler = new GuildHandler(client, guild);
                _instances.Add(guild.Id, nHandler);
                return nHandler;
            }


            return null;
        }
                
    }
}
