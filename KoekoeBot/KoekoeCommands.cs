using System.Collections.Generic;

namespace KoekoeBot
{

    using System;
    using System.ComponentModel;
    using System.Linq;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using DSharpPlus.Commands;
    using DSharpPlus.Commands.ArgumentModifiers;
    using DSharpPlus.Commands.Processors.TextCommands;
    using DSharpPlus.Entities;
    using DSharpPlus.Voice;
    using System.Net;
    using System.Text;
    using Microsoft.Extensions.Logging;

    class KoekoeCommands
    {

        [Command("register"), Description("registers your current voice channel as a channel to anounce in.")]
        public static async Task RegisterChannel(CommandContext ctx)
        {
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.ChannelId == null)
            {
                // they did not specify a channel and are not in one
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            DiscordChannel voiceChannel = await vstat.GetChannelAsync();
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, voiceChannel.Guild, true);
            if (handler != null)
                handler.AddChannel(voiceChannel); //Could throw access violation because we run the handlers async, this needs fixing

            await ctx.RespondAsync($"Registered to `{voiceChannel.Name}`");
        }

        [Command("unregister"), Description("removes registration from your current voice channel.")]
        public static async Task UnregisterChannel(CommandContext ctx)
        {
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.ChannelId == null)
            {
                // they did not specify a channel and are not in one
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            DiscordChannel voiceChannel = await vstat.GetChannelAsync();
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, voiceChannel.Guild, false);
            if (handler != null)
            {
                handler.RemoveChannel(voiceChannel); //Could throw access violation because we run the handlers async, this needs fixing
                await ctx.RespondAsync($"Unregistered `{voiceChannel.Name}`");
                return;
            }

            await ctx.RespondAsync("No channels registered yet.");
        }


        [Command("listregister"), Description("lists all registered voice channels.")]
        public static async Task ListRegister(CommandContext ctx)
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
        public static async Task ListAlarm(CommandContext ctx)
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
        public static async Task CancelAlarm(CommandContext ctx, [Description("Name of the alarm to cancel")] string alarmname)
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
        public static async Task SetAlarm(CommandContext ctx, [Description("name of the new alarm")] string alarmname, [Description("id of sample to play on alarm")] string sampleidstr, [RemainingText, Description("Alarm time ex 4:20 or 15:34")] string datestring)
        {
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.ChannelId == null)
            {
                // they did not specify a channel and are not in one
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            DiscordChannel voiceChannel = await vstat.GetChannelAsync();

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

                        GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, voiceChannel.Guild, true);
                        int sampleId = -1;
                        bool hasSample = int.TryParse(sampleidstr, out sampleId);


                        handler.AddAlarm(dt, alarmname, hasSample ? (int?)sampleId : null, ctx.User.Id);
                    }
                }

            }

            await ctx.RespondAsync($"Registered alarm `{alarmname}` to `{ctx.User.Username}`");
        }

        [Command("add"), Description("Add a new sample by attaching an mp3 file to your message")]
        public static async Task AddSample(CommandContext ctx, [RemainingText, Description("a name for the new sample")] string samplename)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);
            if (handler == null)
            {
                await ctx.RespondAsync($"I can't run this command from here - ask me in a discord server.");
                return;
            }

            if (samplename.Length == 0)
            {
                await ctx.RespondAsync("Specify a name: `!kk add {samplename}`");
                return;
            }

            if (ctx is not TextCommandContext textCtx || textCtx.Message.Attachments.Count == 0 || !textCtx.Message.Attachments[0].FileName.EndsWith(".mp3"))
            {
                await ctx.RespondAsync($"No file attached, attach a mp3 file to your message");
                return;
            }

            string samplepath = Path.Join(handler.getSampleBasePath(), handler.getFileNameForSampleName(samplename));
            using (var client = new WebClient())
            {
                client.DownloadFile(new System.Uri(textCtx.Message.Attachments[0].ProxyUrl), $"{samplepath}");
            }

            SampleData sample = handler.AddSampleFromFile(samplepath, samplename);

            handler.SaveGuildData();

            await ctx.RespondAsync($"Added {samplename} use !kk p [{String.Join(',', sample.SampleAliases)},{sample.Name}] to play the sample in your current voice channel");
        }

        //TODO: remove or maybe limit these debug commands
        [Command("Announce"), Description("DEBUG: Announces an audio file to all registered channels in the sender's guild")]
        public static async Task Announce(CommandContext ctx, [RemainingText, Description("path to the file to play.")] string filename)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);
            if (handler != null)
            {
                await ctx.RespondAsync($"Will announce `{filename}`");
                handler.AnnounceFile(filename);
                await ctx.FollowupAsync($"Done announcing `{filename}`");
            }

        }

        [Command("alias"), Description("add an alias for a sample")]
        public static async Task AddAlias(CommandContext ctx, [Description("the sample to create an alias for")] string samplename, [RemainingText, Description("an alias for the given sample")] string alias)
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
        public static async Task RemoveAlias(CommandContext ctx, [Description("the sample to create an alias for")] string samplename, [RemainingText, Description("an alias for the given sample")] string alias)
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
        public static async Task Search(CommandContext ctx, [RemainingText, Description("a search term")] string searchQuery)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            List<SampleData> samples = handler.GetGuildData().samples
                .Where(x => x.exists)
                .Where(x => x.Name.Contains(searchQuery) || x.SampleAliases.Where(x => x.Contains(searchQuery)).Any())
                .OrderBy((x) => int.Parse(x.SampleAliases[0]))
                .ToList();

            int max_rows = 50;
            bool first = true;

            for (int i = 0; i < samples.Count; i += max_rows)
            {
                StringBuilder tableBuilder = AsciiTableGenerators.AsciiTableGenerator.CreateAsciiTableFromValues(samples.Skip(i).Take(max_rows).Select(x => new string[] { x.SampleAliases[0], x.Name, x.PlayCount.ToString(), String.Join(',', x.SampleAliases.Skip(1)), x.enabled.ToString() }).ToArray(), new string[] { "Id", "Name", "PlayCount", "Aliases", "Enabled" });

                string content = $"```Koekoe search result:\n\n{tableBuilder.ToString()}```";
                if (first)
                {
                    await ctx.RespondAsync(content);
                    first = false;
                }
                else
                {
                    await ctx.FollowupAsync(content);
                }
            }

            if (first)
                await ctx.RespondAsync("No samples found.");
        }

        [Command("samples"), Description("List available samples")]
        public static async Task Samples(CommandContext ctx)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            List<SampleData> samples = handler.GetGuildData().samples.Where(x => x.exists).OrderBy((x) => int.Parse(x.SampleAliases[0])).ToList();

            const int ROWS = 50;
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
            //Send remaining
            await ctx.RespondAsync($"```{content}```");
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
                    await ctx.FollowupAsync($"```{content}```");
                    content = "";
                }
            }

            //Send remaining content in buffer
            if (content.Length > 0)
            {
                await ctx.FollowupAsync($"```{content}```");
            }

        }

        [Command("updatesamples"), Description("Update the list of available samples")]
        public static async Task UpdateSamples(CommandContext ctx)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            handler.UpdateSamplelist();
            await ctx.RespondAsync("Sample list updated");
        }

        [Command("p"), Description("Shortcut to play samples, use !kk samples command to see a list of available samples")]
        public static async Task p(CommandContext ctx, [RemainingText, Description("sample number from !kk samples command")] string sampleNameOrAlias)
        {
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.ChannelId == null)
            {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);

            SampleData sample = handler.getSample(sampleNameOrAlias);
            if (sample != null && sample.enabled)
            {
                List<DiscordChannel> channels = new List<DiscordChannel>();
                channels.Add(await vstat.GetChannelAsync());
                handler.AnnounceSample(sampleNameOrAlias, 1, channels); //each sample has it's sample number as an alias
                await ctx.RespondAsync($"Playing {sampleNameOrAlias}");
            }
            else
            {
                await ctx.RespondAsync($"{sampleNameOrAlias} {(sample == null ? "does not exist" : "is disabled")} :(");
            }


        }

        //Used for debugging the DSharpPlus.Voice and ffmpeg stuff
        [Command("play"), Description("DEBUG: Plays an audio file.")]
        public static async Task Play(CommandContext ctx, [RemainingText, Description("path to the file to play.")] string filename)
        {
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.ChannelId == null)
            {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            if (!File.Exists(filename))
            {
                await ctx.RespondAsync($"Will not be playing {filename} (file not found)");
                ctx.Client.Logger.LogWarning($"Will not be playing {filename} (file not found)");
                return;
            }

            DiscordChannel channel = await vstat.GetChannelAsync();

            // connect
            VoiceConnection vnc = await channel.ConnectAsync();

            Exception exc = null;

            try
            {
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

                AudioWriter writer = vnc.CreateAudioWriter(AudioFormat.S16LE48KHzStereoPCM);
                var txStream = writer.AsStream();
                await ffout.CopyToAsync(txStream);
                await txStream.FlushAsync();
                writer.SignalCompletion();

            }
            catch (Exception ex) { exc = ex; }
            finally
            {
                await vnc.DisposeAsync();
                await ctx.RespondAsync($"Finished playing `{filename}`");
            }

            if (exc != null)
                await ctx.FollowupAsync($"An exception occured during playback: `{exc.GetType()}: {exc.Message}`");
        }
    }
}
