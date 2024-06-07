using System.Collections.Generic;

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
    using Microsoft.Extensions.Logging;
    using DSharpPlus.SlashCommands;
    using DSharpPlus.Interactivity.Extensions;
    using DSharpPlus;

    class KoekoeSlashCommands : ApplicationCommandModule
    {

        [SlashCommand("register", "registers your current voice channel as a channel to anounce in.")]
        public async Task RegisterChannel(InteractionContext ctx)
        {
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.Channel == null)
            {
                // they did not specify a channel and are not in one
                await ctx.CreateResponseAsync("You are not in a voice channel.");
                return;
            }

            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, vstat.Channel.Guild, true);
            if (handler != null)
                handler.AddChannel(vstat.Channel); //Could throw access violation because we run the handlers async, this needs fixing

            await ctx.CreateResponseAsync($"Registered to `{vstat.Channel.Name}`");
        }

        [SlashCommand("unregister", "removes registration from your current voice channel.")]
        public async Task UnregisterChannel(InteractionContext ctx)
        {
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.Channel == null)
            {
                // they did not specify a channel and are not in one
                await ctx.CreateResponseAsync("You are not in a voice channel.");
                return;
            }

            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, vstat.Channel.Guild, false);
            if (handler != null)
            {
                handler.RemoveChannel(vstat.Channel); //Could throw access violation because we run the handlers async, this needs fixing
                await ctx.CreateResponseAsync($"Unregistered `{vstat.Channel.Name}`");
                return;
            }

            await ctx.CreateResponseAsync("No channels registered yet.");
        }


        [SlashCommand("listregister", "lists all registered voice channels.")]
        public async Task ListRegister(InteractionContext ctx)
        {

            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Channel.Guild, false);
            if (handler != null)
            {
                string[] names = await handler.GetRegisteredChannelNames();
                string channelstext = String.Join("`, `", names);

                await ctx.CreateResponseAsync($"Currently registered to: `{channelstext}`");
                return;
            }


            await ctx.CreateResponseAsync($"Currently not registered to any channel, use `!kk register` while in a voice channel to add it.");
        }


        [SlashCommand("listalarms", "lists all registered alarms.")]
        public async Task ListAlarm(InteractionContext ctx)
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

                await ctx.CreateResponseAsync($"Alarms:\n{alarmstext}");
                return;
            }


            await ctx.CreateResponseAsync($"Currently not registered to any channel, use `!kk register` while in a voice channel to add it.");
        }

        [SlashCommand("cancelalarm", "cancels an alarm by name, you can only cancel your own alarms")]
        public async Task CancelAlarm(InteractionContext ctx, [Option("alarmname", "Name of the alarm to cancel")] string alarmname)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            List<AlarmData> alarms = handler.GetAlarms();
            for (int i = 0; i < alarms.Count; i++)
            {
                if (alarms[i].AlarmName == alarmname && alarms[i].userId == ctx.User.Id)
                {
                    alarms.RemoveAt(i);
                    handler.SaveGuildData();
                    await ctx.CreateResponseAsync($"Canceled alarm `{alarmname}`");

                    return;
                }
            }

            await ctx.CreateResponseAsync($"Couldn't find alarm `{alarmname}`");
        }


        [SlashCommand("setalarm", "set an alarm for your current voicechannel")]
        public async Task SetAlarm(InteractionContext ctx, [Option("alarmname", "name of the new alarm")] string alarmname, [Option("sampleid", "id of sample to play on alarm")] string sampleidstr, [Option("alarmtime", "Alarm time ex: 4:20 or 15:34")] string datestring)
        {
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.Channel == null)
            {
                // they did not specify a channel and are not in one
                await ctx.CreateResponseAsync("You are not in a voice channel.");
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
                    }
                }

            }

            await ctx.CreateResponseAsync($"Registered alarm `{alarmname}` to `{ctx.User.Username}`");
        }

        [SlashCommand("add", "add new sample by attaching a file to this message")]
        public async Task AddSample(InteractionContext ctx, [Option("samplename", "name of the new sample")] string samplename)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);
            if (handler != null)
            {
                if (samplename.Length == 0)
                {
                    await ctx.CreateResponseAsync("Specify a name: `!kk add {samplename}`");
                    return;
                }
                DiscordMessage message = await ctx.GetOriginalResponseAsync();
                if (message.Attachments.Count > 0 && message.Attachments[0].FileName.EndsWith(".mp3"))
                {
                    string samplepath = Path.Join(handler.getSampleBasePath(), handler.getFileNameForSampleName(samplename));
                    using (var client = new WebClient())
                    {
                        client.DownloadFile(new System.Uri(message.Attachments[0].ProxyUrl), $"{samplepath}");
                    }

                    SampleData sample = handler.AddSampleFromFile(samplepath, samplename);

                    handler.SaveGuildData();

                    await ctx.CreateResponseAsync($"Added {samplename} use !kk p [{String.Join(',', sample.SampleAliases)},{sample.Name}] to play the sample in your current voice channel");
                }
                else
                {
                    await ctx.CreateResponseAsync($"No file attached, attach a mp3 file to your message");
                }
            }
            else
            {
                await ctx.CreateResponseAsync($"I can't run this command from here - ask me in a discord server.");
            }

        }

        [SlashCommand("alias", "add an alias for a sample")]
        public async Task AddAlias(InteractionContext ctx, [Option("samplename", "the sample to create an alias for")] string samplename, [Option("alias", "create a new alias for a given sample")] string alias)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);

            if (handler.AddAlias(samplename, alias))
            {
                await ctx.CreateResponseAsync($"Added {alias} as an alias for {samplename}");
            }
            else
            {
                await ctx.CreateResponseAsync($"{alias} is already taken by another sample: {handler.getSample(alias).Name}");
            }
        }

        [SlashCommand("removealias", "remove an alias from a sample")]
        public async Task RemoveAlias(InteractionContext ctx, [Option("samplename", "the sample to create an alias for")] string samplename, [Option("alias", "alias to remove")] string alias)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);

            if (handler.RemoveAlias(samplename, alias))
            {
                await ctx.CreateResponseAsync($"Removed {alias} as an alias for {samplename}");
            }
            else
            {
                await ctx.CreateResponseAsync($"{alias} is not an alias for {samplename}");
            }
        }

        [SlashCommand("search", "Search for samples")]
        public async Task Search(InteractionContext ctx, [Option("query", "search query")] string searchQuery)
        {
            // Acknowledge the command
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                .WithContent("Preparing result..."));

            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            List<SampleData> samples = handler.GetGuildData().samples
                .Where(x => x.exists)
                .Where(x => x.Name.Contains(searchQuery) || x.SampleAliases.Where(x => x.Contains(searchQuery)).Any())
                .OrderBy((x) => int.Parse(x.SampleAliases[0]))
                .ToList();


            var interactivity = ctx.Client.GetInteractivity();
            StringBuilder tableBuilder = AsciiTableGenerators.AsciiTableGenerator.CreateAsciiTableFromValues(samples.Select(x => new string[] { x.SampleAliases[0], x.Name, x.PlayCount.ToString(), String.Join(',', x.SampleAliases.Skip(1)), x.enabled.ToString() }).ToArray(), new string[] { "Id", "Name", "PlayCount", "Aliases", "Enabled" });

            var pages = interactivity.GeneratePagesInEmbed(tableBuilder.ToString());
            await interactivity.SendPaginatedMessageAsync(ctx.Channel, ctx.User, pages);

            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Done!"));
        }

        [SlashCommand("samples", "List available samples")]
        public async Task Samples(InteractionContext ctx)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            List<SampleData> samples = handler.GetGuildData().samples.Where(x => x.exists).OrderBy((x) => int.Parse(x.SampleAliases[0])).ToList();

            // Acknowledge the command
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                .WithContent("Preparing result..."));

            const int COLS = 2;
            const int COL_WIDTH = 40;

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
            }


            var interactivity = ctx.Client.GetInteractivity();
            var pages = interactivity.GeneratePagesInEmbed(content);
            await interactivity.SendPaginatedMessageAsync(ctx.Channel, ctx.User, pages, timeoutoverride: TimeSpan.FromMinutes(2));

            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Done!"));
        }

        [SlashCommand("updatesamples", "Debug command; updates the list of available samples")]
        public async Task UpdateSamples(InteractionContext ctx)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            handler.UpdateSamplelist();
            await ctx.CreateResponseAsync("Sample list updated");
        }


        [Command("play"), Description("Play a sample, use !kk samples command to see a list of available samples")]
        public async Task p(InteractionContext ctx, [Option("nameoralias", "sample number from !kk samples command or alias")] string sampleNameOrAlias)
        {
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.Channel == null)
            {
                await ctx.CreateResponseAsync("You are not in a voice channel.");
                return;
            }

            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);

            SampleData sample = handler.getSample(sampleNameOrAlias);
            if (sample != null && sample.enabled)
            {
                List<DiscordChannel> channels = new List<DiscordChannel>();
                channels.Add(vstat.Channel);
                handler.AnnounceSample(sampleNameOrAlias, 1, channels); //each sample has it's sample number as an alias
            }
            else
            {
                await ctx.CreateResponseAsync($"{sampleNameOrAlias} {(sample == null ? "does not exist" : "is disabled")} :(");
            }


        }

        //Used for debugging the voicenext and ffmpeg stuff
        [Command("playfile"), Description("DEBUG: Plays audio by filename.")]
        public async Task Play(InteractionContext ctx, [Option("filepath", "path to the file to play.")] string filename)
        {
            await ctx.DeferAsync();
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.Channel == null)
            {
                await ctx.CreateResponseAsync("You are not in a voice channel.");
                return;
            }

            if (!File.Exists(filename))
            {
                await ctx.CreateResponseAsync($"Will not be playing {filename} (file not found)");
                ctx.Client.Logger.LogWarning($"Will not be playing {filename} (file not found)");
            }

            DiscordChannel Channel = vstat.Channel;

            // check whether VNext is enabled
            var vnext = ctx.Client.GetVoiceNext();
            if (vnext == null)
            {
                ctx.Client.Logger.LogWarning("VoiceNext not configured");
                return;
            }

            // check whether we aren't already connected
            var vnc = vnext.GetConnection(Channel.Guild);
            if (vnc != null)
            {
                ctx.Client.Logger.LogWarning("Already connected in this guild.");
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

                ctx.Client.Logger.LogInformation($"Will run {psi.FileName} as {psi.Arguments}");

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
                //await ctx.CreateResponseAsync($"Finished playing `{filename}`");
            }

            if (exc != null) // TODO: do this in the exception handler?
                await ctx.CreateResponseAsync($"An exception occured during playback: `{exc.GetType()}: {exc.Message}`");
        }
    }
}

