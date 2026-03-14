using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext.Attributes; // [RemainingText]
using DSharpPlus.CommandsNext;

using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;                                    // GetVoiceNext()
using Microsoft.Extensions.Logging;

namespace KoekoeBot
{
    class KoekoeCommands
    {
        [Command("register"), System.ComponentModel.Description("registers your current voice channel as a channel to announce in.")]
        public async Task RegisterChannel(CommandContext ctx)
        {
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.ChannelId == null)            // .Channel → .VoiceChannel
            {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            var voiceChannel = await vstat.GetChannelAsync();
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, voiceChannel.Guild, true);
            if (handler != null)
                handler.AddChannel(voiceChannel);

            await ctx.RespondAsync($"Registered to `{voiceChannel.Name}`");
        }

        [Command("unregister"), System.ComponentModel.Description("removes registration from your current voice channel.")]
        public async Task UnregisterChannel(CommandContext ctx)
        {
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.ChannelId == null)
            {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            var voiceChannel = await vstat.GetChannelAsync();
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, voiceChannel.Guild, false);
            if (handler != null)
            {
                handler.RemoveChannel(voiceChannel);
                await ctx.RespondAsync($"Unregistered `{voiceChannel.Name}`");
                return;
            }

            await ctx.RespondAsync("No channels registered yet.");
        }

        [Command("listregister"), System.ComponentModel.Description("lists all registered voice channels.")]
        public async Task ListRegister(CommandContext ctx)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, false);
            if (handler != null)
            {
                string[] names = await handler.GetRegisteredChannelNames();
                await ctx.RespondAsync($"Currently registered to: `{String.Join("`, `", names)}`");
                return;
            }
            await ctx.RespondAsync("Currently not registered to any channel, use `!kk register` while in a voice channel to add it.");
        }

        [Command("listalarms"), System.ComponentModel.Description("lists all registered alarms.")]
        public async Task ListAlarm(CommandContext ctx)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, false);
            if (handler != null)
            {
                List<AlarmData> alarms = handler.GetAlarms();
                string[] alarmtexts = new string[alarms.Count];
                for (int i = 0; i < alarmtexts.Length; i++)
                {
                    DiscordMember member = await ctx.Guild.GetMemberAsync(alarms[i].userId);
                    alarmtexts[i] = $"{member.Username}: {alarms[i].AlarmDate.ToShortTimeString()} ({alarms[i].AlarmName})";
                }
                await ctx.RespondAsync($"Alarms:\n{String.Join("`\n`", alarmtexts)}");
                return;
            }
            await ctx.RespondAsync("Currently not registered to any channel, use `!kk register` while in a voice channel to add it.");
        }

        [Command("cancelalarm"), System.ComponentModel.Description("cancels an alarm by name (you can only cancel your own alarms)")]
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

        [Command("setalarm"), System.ComponentModel.Description("set an alarm for your current voicechannel")]
        public async Task SetAlarm(
            CommandContext ctx,
            string alarmname,
            string sampleidstr,
            [RemainingText, System.ComponentModel.Description("Alarm time e.g. 4:20 or 15:34")] string datestring)
        {
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.ChannelId == null)
            {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            string[] parts = datestring.Split(':');
            if (parts.Length == 2
                && int.TryParse(parts[0], out int parsedHours)
                && int.TryParse(parts[1], out int parsedMinutes)
                && parsedHours > 0 && parsedHours < 24
                && parsedMinutes > 0 && parsedMinutes < 60)
            {
                int hourdiff = (parsedHours - DateTime.Now.Hour) % 24;
                if (hourdiff < 0) hourdiff += 24;
                int mindiff = (parsedMinutes - DateTime.Now.Minute) % 60;
                DateTime dt = DateTime.Now.AddHours(hourdiff).AddMinutes(mindiff).AddSeconds(-DateTime.Now.Second);
                var voiceChannel = await vstat.GetChannelAsync();
                GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, voiceChannel.Guild, true);
                bool hasSample = int.TryParse(sampleidstr, out int sampleId);
                handler.AddAlarm(dt, alarmname, hasSample ? (int?)sampleId : null, ctx.User.Id);
            }

            await ctx.RespondAsync($"Registered alarm `{alarmname}` to `{ctx.User.Username}`");
        }

        [Command("add"), System.ComponentModel.Description("Add a new sample by attaching an mp3 file to your message")]
        public async Task AddSample(
            CommandContext ctx,
            [RemainingText, System.ComponentModel.Description("a name for the new sample")] string samplename)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);
            if (handler == null)
            {
                await ctx.RespondAsync("I can't run this command from here - ask me in a discord server.");
                return;
            }
            if (samplename.Length == 0)
            {
                await ctx.RespondAsync("Specify a name: `!kk add {samplename}`");
                return;
            }

            var textCtx = ctx;
            if (textCtx.Message.Attachments.Count > 0 && textCtx.Message.Attachments[0].FileName.EndsWith(".mp3"))
            {
                string samplepath = Path.Join(handler.getSampleBasePath(), handler.getFileNameForSampleName(samplename));
                using (var httpClient = new HttpClient())
                {
                    var bytes = await httpClient.GetByteArrayAsync(textCtx.Message.Attachments[0].ProxyUrl);
                    await File.WriteAllBytesAsync(samplepath, bytes);
                }
                SampleData sample = handler.AddSampleFromFile(samplepath, samplename);
                handler.SaveGuildData();
                await ctx.RespondAsync($"Added {samplename} — use !kk p [{String.Join(',', sample.SampleAliases)},{sample.Name}] to play it in your current voice channel");
            }
            else
            {
                await ctx.RespondAsync("No file attached, attach an mp3 file to your message");
            }
        }

        [Command("Announce"), System.ComponentModel.Description("DEBUG: Announces an audio file to all registered channels in the sender's guild")]
        public async Task Announce(
            CommandContext ctx,
            [RemainingText, System.ComponentModel.Description("path to the file to play.")] string filename)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);
            if (handler != null)
            {
                await ctx.RespondAsync($"Will announce `{filename}`");
                handler.AnnounceFile(filename);
                await ctx.RespondAsync($"Done announcing `{filename}`");
            }
        }

        [Command("alias"), System.ComponentModel.Description("add an alias for a sample")]
        public async Task AddAlias(
            CommandContext ctx,
            string samplename,
            [RemainingText, System.ComponentModel.Description("an alias for the given sample")] string alias)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);
            if (handler.AddAlias(samplename, alias))
                await ctx.RespondAsync($"Added {alias} as an alias for {samplename}");
            else
                await ctx.RespondAsync($"{alias} is already taken by another sample: {handler.getSample(alias).Name}");
        }

        [Command("removealias"), System.ComponentModel.Description("remove an alias from a sample")]
        public async Task RemoveAlias(
            CommandContext ctx,
            string samplename,
            [RemainingText, System.ComponentModel.Description("an alias for the given sample")] string alias)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);
            if (handler.RemoveAlias(samplename, alias))
                await ctx.RespondAsync($"Removed {alias} as an alias for {samplename}");
            else
                await ctx.RespondAsync($"{alias} is not an alias for {samplename}");
        }

        [Command("search"), System.ComponentModel.Description("Search for samples")]
        public async Task Search(
            CommandContext ctx,
            [RemainingText, System.ComponentModel.Description("a search term")] string searchQuery)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            List<SampleData> samples = handler.GetGuildData().samples
                .Where(x => x.exists)
                .Where(x => x.Name.Contains(searchQuery) || x.SampleAliases.Any(a => a.Contains(searchQuery)))
                .OrderBy(x => int.Parse(x.SampleAliases[0]))
                .ToList();

            const int max_rows = 50;
            for (int i = 0; i < samples.Count; i += max_rows)
            {
                StringBuilder tableBuilder = AsciiTableGenerators.AsciiTableGenerator.CreateAsciiTableFromValues(
                    samples.Skip(i).Take(max_rows)
                        .Select(x => new string[] { x.SampleAliases[0], x.Name, x.PlayCount.ToString(), String.Join(',', x.SampleAliases.Skip(1)), x.enabled.ToString() })
                        .ToArray(),
                    new string[] { "Id", "Name", "PlayCount", "Aliases", "Enabled" });

                await new DiscordMessageBuilder()
                    .WithContent($"```Koekoe search result:\n\n{tableBuilder}```")
                    .SendAsync(ctx.Channel);
            }
        }

        [Command("samples"), System.ComponentModel.Description("List available samples")]
        public async Task Samples(CommandContext ctx)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            List<SampleData> samples = handler.GetGuildData().samples
                .Where(x => x.exists)
                .OrderBy(x => int.Parse(x.SampleAliases[0]))
                .ToList();

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

            await new DiscordMessageBuilder()
                .WithContent($"```{header}\nAvailable Samples,\nuse !kk p {{number}} to play the sample.\n\n```")
                .SendAsync(ctx.Channel);

            string content = "";
            int lastLen = 0;
            for (int i = 0; i < samples.Count + 1; i += COLS)
            {
                for (int j = 0; j < COLS; j++)
                {
                    if (i + j >= samples.Count) break;
                    string entry = $"{samples[i + j].SampleAliases[0]}. {samples[i + j].Name}";
                    content += (j != 0) ? String.Concat(Enumerable.Repeat(" ", COL_WIDTH - lastLen)) + entry : entry;
                    content += (j == COLS - 1) ? "\n" : "";
                    lastLen = entry.Length;
                }

                if (i > COLS && i % ROWS < COLS)
                {
                    await new DiscordMessageBuilder().WithContent($"```{content}```").SendAsync(ctx.Channel);
                    content = "";
                }
            }

            if (content.Length > 0)
                await new DiscordMessageBuilder().WithContent($"```{content}```").SendAsync(ctx.Channel);
        }

        [Command("updatesamples"), System.ComponentModel.Description("Update the list of available samples")]
        public async Task UpdateSamples(CommandContext ctx)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            handler.UpdateSamplelist();
            await ctx.RespondAsync("Sample list updated");
        }

        [Command("p"), System.ComponentModel.Description("Shortcut to play samples, use !kk samples to see a list")]
        public async Task PlaySample(
            CommandContext ctx,
            [RemainingText, System.ComponentModel.Description("sample number from !kk samples")] string sampleNameOrAlias)
        {
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
                var voiceChannel = await vstat.GetChannelAsync();
                handler.AnnounceSample(sampleNameOrAlias, 1, new List<DiscordChannel> { voiceChannel });
            }
            else
                await ctx.RespondAsync($"{sampleNameOrAlias} {(sample == null ? "does not exist" : "is disabled")} :(");
        }

        [Command("play"), System.ComponentModel.Description("DEBUG: Plays an audio file.")]
        public async Task PlayFile(
            CommandContext ctx,
            [RemainingText, System.ComponentModel.Description("path to the file to play.")] string filename)
        {
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

            // GetVoiceNext() is an extension method from DSharpPlus.VoiceNext
            var vnext = ctx.Client.ServiceProvider.GetRequiredService<VoiceNextExtension>();
            if (vnext == null)
            {
                ctx.Client.Logger.LogWarning("VoiceNext not configured");
                return;
            }

            var vnc = vnext.GetConnection(channel.Guild);
            if (vnc != null)
            {
                ctx.Client.Logger.LogWarning("Already connected in this guild.");
                return;
            }

            vnc = await vnext.ConnectAsync(channel);
            while (vnc.IsPlaying)
                await vnc.WaitForPlaybackFinishAsync();

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
                var txStream = vnc.GetTransmitSink();
                await ffmpeg.StandardOutput.BaseStream.CopyToAsync(txStream);
                await txStream.FlushAsync();
                await vnc.WaitForPlaybackFinishAsync();
            }
            catch (Exception ex) { exc = ex; }
            finally
            {
                await vnc.SendSpeakingAsync(false);
                vnext.GetConnection(channel.Guild).Disconnect();
                await ctx.RespondAsync($"Finished playing `{filename}`");
            }

            if (exc != null)
                await ctx.RespondAsync($"An exception occurred during playback: `{exc.GetType()}: {exc.Message}`");
        }
    }
}