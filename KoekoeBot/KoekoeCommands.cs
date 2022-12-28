using System.Collections.Generic;
using System.Data;

namespace KoekoeBot
{

    using System;
    using System.Linq;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using DSharpPlus.CommandsNext;
    using DSharpPlus.CommandsNext.Attributes;
    using DSharpPlus.Entities;
    using DSharpPlus.VoiceNext;
    using System.Net;
    using System.Text;

    class KoekoeCommands : BaseCommandModule
    {

        [Command("register"), Description("registers your current voice channel as a channel to anounce in.")]
        public async Task RegisterChannel(CommandContext ctx, DiscordChannel channel = null)
        {
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.Channel == null)
            {
                // they did not specify a channel and are not in one
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, vstat.Channel.Guild, true);
            if (handler != null)
                handler.AddChannel(vstat.Channel); //Could throw access violation because we run the handlers async, this needs fixing

            if (!handler.IsRunning)
                handler.Execute(); //Will run async


            await ctx.RespondAsync($"Registered to `{vstat.Channel.Name}`");
        }

        [Command("unregister"), Description("removes registration from your current voice channel.")]
        public async Task UnregisterChannel(CommandContext ctx, DiscordChannel channel = null)
        {
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.Channel == null)
            {
                // they did not specify a channel and are not in one
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, vstat.Channel.Guild, false);
            if (handler != null)
            {
                handler.RemoveChannel(vstat.Channel); //Could throw access violation because we run the handlers async, this needs fixing
                await ctx.RespondAsync($"Unregistered `{vstat.Channel.Name}`");
                return;
            }

            await ctx.RespondAsync("No channels registered yet.");
        }


        [Command("listregister"), Description("lists all registered voice channels.")]
        public async Task ListRegister(CommandContext ctx, DiscordChannel channel = null)
        {

            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Channel.Guild, false);
            if (handler != null)
            {
                string[] names = await handler.GetRegisteredChannelNames();
                string channelstext = String.Join("`, `", names);

                await ctx.RespondAsync($"Currently registered to: `{channelstext}`");
                return;
            }


            await ctx.RespondAsync($"Currently not registered to any channel, use `!kk register` while in a voice channel to add it.");
        }


        [Command("listalarms"), Description("lists all registered alarms.")]
        public async Task ListAlarm(CommandContext ctx, DiscordChannel channel = null)
        {

            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Channel.Guild, false);
            if (handler != null)
            {
                List<AlarmData> alarms = handler.GetAlarms();
                string[] alarmtexts = new string[alarms.Count];
                for (int i = 0; i < alarmtexts.Length; i++)
                {
                    DiscordMember member = await ctx.Guild.GetMemberAsync(alarms[i].userId);
                    alarmtexts[i] = $"{member.Username}: {alarms[i].AlarmDate.ToShortTimeString()} ({alarms[i].AlarmName})";
                }
                string alarmstext = String.Join("`\n`", alarmtexts);

                await ctx.RespondAsync($"Alarms:\n{alarmstext}");
                return;
            }


            await ctx.RespondAsync($"Currently not registered to any channel, use `!kk register` while in a voice channel to add it.");
        }

        [Command("cancelalarm"), Description("cancels an alarm by name (you can only cancel your own alarms)")]
        public async Task CancelAlarm(CommandContext ctx, string alarmname)
        {

            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            List<AlarmData> alarms = handler.GetAlarms();
            for (int i = 0; i < alarms.Count; i++)
            {
                if (alarms[i].AlarmName == alarmname && alarms[i].userId == ctx.User.Id)
                {
                    alarms.RemoveAt(i);
                    handler.SaveGuildData();
                    await ctx.RespondAsync($"Canceled alarm `{alarmname}`");

                    return;
                }
            }

            await ctx.RespondAsync($"Couldn't find alarm `{alarmname}`");
        }


        [Command("setalarm"), Description("set an alarm for your current voicechannel")]
        public async Task SetAlarm(CommandContext ctx, string alarmname, string sampleidstr, [RemainingText, Description("Alarm time ex 4:20 or 15:34")] string datestring)
        {
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.Channel == null)
            {
                // they did not specify a channel and are not in one
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            string[] datestringComps = datestring.Split(':');
            if (datestringComps.Length == 2)
            {
                int parsedHours, parsedMinutes;
                if (int.TryParse(datestringComps[0], out parsedHours) && int.TryParse(datestringComps[1], out parsedMinutes))
                {
                    if (parsedHours > 0 && parsedHours < 24 && parsedMinutes > 0 && parsedMinutes < 60)
                    {
                        int hourdiff = (parsedHours - DateTime.Now.Hour) % 24;
                        if (hourdiff < 0)
                            hourdiff += 24;

                        int mindiff = (parsedMinutes - DateTime.Now.Minute) % 60;
                        DateTime dt = DateTime.Now.AddHours(hourdiff).AddMinutes(mindiff).AddSeconds(-DateTime.Now.Second);

                        GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, vstat.Channel.Guild, true);
                        int sampleId = -1;
                        bool hasSample = int.TryParse(sampleidstr, out sampleId);


                        handler.AddAlarm(dt, alarmname, hasSample ? (int?)sampleId : null, ctx.User.Id);

                        if (!handler.IsRunning) //If the handler isn't running for some reason start it TODO: remove these, or implement handler sleep 
                            handler.Execute(); //Will run async
                    }
                }

            }

            await ctx.RespondAsync($"Registered alarm `{alarmname}` to `{ctx.User.Username}`");
        }

        [Command("add"), Description("Add a new sample by attaching an mp3 file to your message")]
        public async Task AddSample(CommandContext ctx, [RemainingText, Description("a name for the new sample")] string samplename)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);
            if (handler != null)
            {
                if (samplename.Length == 0)
                {
                    await ctx.Message.RespondAsync("Specify a name: `!kk add {samplename}`");
                    return;
                }

                if (ctx.Message.Attachments.Count > 0 && ctx.Message.Attachments[0].FileName.EndsWith(".mp3"))
                {
                    string samplepath = Path.Join(handler.getSampleBasePath(), handler.getFileNameForSampleName(samplename));
                    using (var client = new WebClient())
                    {
                        client.DownloadFile(new System.Uri(ctx.Message.Attachments[0].ProxyUrl), $"{samplepath}");
                    }

                    SampleData sample = handler.AddSampleFromFile(samplepath, samplename);

                    handler.SaveGuildData();

                    await ctx.Message.RespondAsync($"Added {samplename} use !kk p [{String.Join(',', sample.SampleAliases)},{sample.Name}] to play the sample in your current voice channel");
                }
                else
                {
                    await ctx.Message.RespondAsync($"No file attached, attach a mp3 file to your message");
                }
            }
            else
            {
                await ctx.Message.RespondAsync($"I can't run this command from here - ask me in a discord server.");
            }

        }

        //TODO: remove or maybe limit these debug commands
        [Command("Announce"), Hidden, Description("DEBUG: Announces an audio file to all registered channels in the sender's guild")]
        public async Task Announce(CommandContext ctx, [RemainingText, Description("path to the file to play.")] string filename)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);
            if (handler != null)
            {
                await ctx.RespondAsync($"Will announce `{filename}`");
                await handler.AnnounceFile(filename);
                await ctx.RespondAsync($"Done announcing `{filename}`");
            }

        }

        // [Command("cleardata"), Description("clear all data from this guild")]
        // public async Task ClearData(CommandContext ctx)
        // {
        //     GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);
        //     handler.ClearGuildData();
        //     await ctx.RespondAsync($"Cleared all data for this guild");
        // }

        [Command("alias"), Description("add an alias for a sample")]
        public async Task AddAlias(CommandContext ctx, string samplename, [RemainingText, Description("an alias for the given sample")] string alias)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);

            if (handler.AddAlias(samplename, alias))
            {
                await ctx.RespondAsync($"Added {alias} as an alias for {samplename}");
            }
            else
            {
                await ctx.RespondAsync($"{alias} is already taken by another sample: {handler.getSample(alias).Name}");
            }
        }

        [Command("removealias"), Description("remove an alias from a sample")]
        public async Task RemoveAlias(CommandContext ctx, string samplename, [RemainingText, Description("an alias for the given sample")] string alias)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);

            if (handler.RemoveAlias(samplename, alias))
            {
                await ctx.RespondAsync($"Removed {alias} as an alias for {samplename}");
            }
            else
            {
                await ctx.RespondAsync($"{alias} is not an alias for {samplename}");
            }
        }

        [Command("search"), Description("Search for samples")]
        public async Task Search(CommandContext ctx, [RemainingText, Description("a search term")] string searchQuery)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            List<SampleData> samples = handler.GetGuildData().samples
                .Where(x => x.exists)
                .Where(x => x.Name.Contains(searchQuery) || x.SampleAliases.Where(x => x.Contains(searchQuery)).Any())
                .OrderBy((x) => int.Parse(x.SampleAliases[0]))
                .ToList();

            int max_rows = 50;
            DiscordMessageBuilder builder = new DiscordMessageBuilder();
            
            for (int i = 0; i < samples.Count; i+=max_rows)
            {
                // SampleData sample = samples[i];

                // content += $"{sample.SampleAliases[0]}. {sample.Name}\t|\t {sample.PlayCount} plays\t|\t Aliases: {String.Join(',', sample.SampleAliases.Skip(1))} \t|\t {(sample.enabled ? "ENABLED" : "DISABLED")}\n";
                StringBuilder tableBuilder = AsciiTableGenerators.AsciiTableGenerator.CreateAsciiTableFromValues(samples.Skip(i).Take(max_rows).Select(x=> new string[] {x.SampleAliases[0], x.Name, x.PlayCount.ToString(), String.Join(',',x.SampleAliases.Skip(1)), x.enabled.ToString()}).ToArray(), new string[] {"Id", "Name", "PlayCount", "Aliases", "Enabled"});
                
                builder.Content = $"```Koekoe search result:\n\n{tableBuilder.ToString()}```";
                await builder.SendAsync(ctx.Channel);
                builder = new DiscordMessageBuilder();                
            }
        }

        [Command("samples"), Description("List available samples")]
        public async Task Samples(CommandContext ctx)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            List<SampleData> samples = handler.GetGuildData().samples.Where(x => x.exists).OrderBy((x) => int.Parse(x.SampleAliases[0])).ToList();

            const int ROWS = 50;
            const int COLS = 2;
            const int COL_WIDTH = 40;

            DiscordMessageBuilder builder = new DiscordMessageBuilder();
            string header = @"
  ▄█   ▄█▄  ▄██████▄     ▄████████    ▄█   ▄█▄  ▄██████▄     ▄████████ 
  ███ ▄███▀ ███    ███   ███    ███   ███ ▄███▀ ███    ███   ███    ███ 
  ███▐██▀   ███    ███   ███    █▀    ███▐██▀   ███    ███   ███    █▀  
 ▄█████▀    ███    ███  ▄███▄▄▄      ▄█████▀    ███    ███  ▄███▄▄▄     
▀▀█████▄    ███    ███ ▀▀███▀▀▀     ▀▀█████▄    ███    ███ ▀▀███▀▀▀     
  ███▐██▄   ███    ███   ███    █▄    ███▐██▄   ███    ███   ███    █▄  
  ███ ▀███▄ ███    ███   ███    ███   ███ ▀███▄ ███    ███   ███    ███ 
  ███   ▀█▀  ▀██████▀    ██████████   ███   ▀█▀  ▀██████▀    ██████████ 
  ▀  ";
            string content = $"{header}\nAvailable Samples,\nuse !kk p {"number"} to play the sample.\n\n";
            //Send remaining
            builder.Content = $"```{content}```";
            await builder.SendAsync(ctx.Channel);
            builder = new DiscordMessageBuilder();
            content = "";

            int lastLen = 0;
            for (int i = 0; i < samples.Count + 1; i += COLS)
            {

                for (int j = 0; j < COLS; j++)
                {
                    if (i + j >= samples.Count)
                        break;

                    string entry = $"{samples[i + j].SampleAliases[0]}. {samples[i + j].Name}";

                    content += (j != 0) ? String.Concat(Enumerable.Repeat(" ", COL_WIDTH - lastLen)) + $"{entry}" : $"{entry}";
                    content += (j == COLS - 1) ? $"\n" : "";

                    lastLen = entry.Length;
                }

                if (i > COLS && i % ROWS < COLS) // Limit the number of rows in a single message
                {
                    builder.Content = $"```{content}```";
                    await builder.SendAsync(ctx.Channel);
                    builder = new DiscordMessageBuilder();
                    content = "";
                }
            }

            //Send remaining content in buffer
            if (content.Length > 0)
            {
                builder.Content = $"```{content}```";
                await builder.SendAsync(ctx.Channel);
            }

        }

        [Command("updatesamples"), Description("Update the list of available samples")]
        public async Task UpdateSamples(CommandContext ctx)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            handler.UpdateSamplelist();
            await ctx.RespondAsync("Sample list updated");
        }

        [Command("p"), Description("Shortcut to play samples, use !kk samples command to see a list of available samples")]
        public async Task p(CommandContext ctx, [RemainingText, Description("sample number from !kk samples command")] string sampleNameOrAlias)
        {
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.Channel == null)
            {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);

            SampleData sample = handler.getSample(sampleNameOrAlias);
            if(sample != null && sample.enabled){
                List<DiscordChannel> channels = new List<DiscordChannel>();
                channels.Add(vstat.Channel);
                await handler.AnnounceSample(sampleNameOrAlias, 1, channels); //each sample has it's sample number as an alias
            } else {
                await ctx.RespondAsync($"{sampleNameOrAlias} {(sample == null ? "does not exist" : "is disabled")} :(");
            }
            
            
        }

        //Used for debugging the voicenext and ffmpeg stuff
        [Command("play"), Hidden, Description("DEBUG: Plays an audio file.")]
        public async Task Play(CommandContext ctx, [RemainingText, Description("path to the file to play.")] string filename)
        {
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.Channel == null)
            {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            if (!File.Exists(filename))
            {
                await ctx.RespondAsync($"Will not be playing {filename} (file not found)");
                System.Console.WriteLine($"Will not be playing {filename} (file not found)");
            }

            DiscordChannel Channel = vstat.Channel;

            // check whether VNext is enabled
            var vnext = ctx.Client.GetVoiceNext();
            if (vnext == null)
            {
                System.Console.WriteLine("VoiceNext not configured");
                return;
            }

            // check whether we aren't already connected
            var vnc = vnext.GetConnection(Channel.Guild);
            if (vnc != null)
            {
                System.Console.WriteLine("Already connected in this guild.");
                return;
            }

            // connect
            vnc = await vnext.ConnectAsync(Channel);

            // wait for current playback to finish
            while (vnc.IsPlaying)
            {
                await vnc.WaitForPlaybackFinishAsync();
            }

            Exception exc = null;

            try
            {
                await vnc.SendSpeakingAsync(true);

                var psi = new ProcessStartInfo
                {
                    FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "ffmpeg" : "ffmpeg.exe",
                    Arguments = $@"-i ""{filename}"" -ac 2 -f s16le -ar 48000 pipe:1 -loglevel quiet",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true

                };

                System.Console.WriteLine($"Will run {psi.FileName} as {psi.Arguments}");

                var ffmpeg = Process.Start(psi);
                var ffout = ffmpeg.StandardOutput.BaseStream;

                var txStream = vnc.GetTransmitSink();
                await ffout.CopyToAsync(txStream);
                await txStream.FlushAsync();
                await vnc.WaitForPlaybackFinishAsync();

            }
            catch (Exception ex) { exc = ex; }
            finally
            {
                await vnc.SendSpeakingAsync(false);
                vnext.GetConnection(Channel.Guild).Disconnect();
                await ctx.Message.RespondAsync($"Finished playing `{filename}`");
            }

            if (exc != null)
                await ctx.RespondAsync($"An exception occured during playback: `{exc.GetType()}: {exc.Message}`");
        }
    }
}

