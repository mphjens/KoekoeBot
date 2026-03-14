using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.Commands;                                          // [Command], CommandContext
using DSharpPlus.Commands.Processors.SlashCommands;                // SlashCommandContext
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Enums;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.VoiceNext;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KoekoeBot
{
    // No base class in v5
    class KoekoeSlashCommands
    {
        [Command("register"), System.ComponentModel.Description("registers your current voice channel as a channel to announce in.")]
        public async Task RegisterChannel(CommandContext ctx)
        {
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.ChannelId == null)
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
            await ctx.RespondAsync("Currently not registered to any channel, use `/register` while in a voice channel to add it.");
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
            await ctx.RespondAsync("Currently not registered to any channel, use `/register` while in a voice channel to add it.");
        }

        [Command("cancelalarm"), System.ComponentModel.Description("cancels an alarm by name (you can only cancel your own alarms)")]
        public async Task CancelAlarm(
            CommandContext ctx,
            [System.ComponentModel.Description("Name of the alarm to cancel")] string alarmname)
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
            [System.ComponentModel.Description("name of the new alarm")] string alarmname,
            [System.ComponentModel.Description("id of sample to play on alarm")] string sampleidstr,
            [System.ComponentModel.Description("Alarm time e.g. 4:20 or 15:34")] string datestring)
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

        [Command("alias"), System.ComponentModel.Description("add an alias for a sample")]
        public async Task AddAlias(
            CommandContext ctx,
            [System.ComponentModel.Description("the sample to create an alias for")] string samplename,
            [System.ComponentModel.Description("new alias to assign")] string alias)
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
            [System.ComponentModel.Description("the sample to remove an alias from")] string samplename,
            [System.ComponentModel.Description("alias to remove")] string alias)
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
            [System.ComponentModel.Description("search query")] string searchQuery)
        {
            await ctx.DeferResponseAsync();

            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            List<SampleData> samples = handler.GetGuildData().samples
                .Where(x => x.exists)
                .Where(x => x.Name.Contains(searchQuery) || x.SampleAliases.Any(a => a.Contains(searchQuery)))
                .OrderBy(x => int.Parse(x.SampleAliases[0]))
                .ToList();

            var interactivity = ctx.Client.ServiceProvider.GetRequiredService<InteractivityExtension>();
            StringBuilder tableBuilder = AsciiTableGenerators.AsciiTableGenerator.CreateAsciiTableFromValues(
                samples.Select(x => new string[] { x.SampleAliases[0], x.Name, x.PlayCount.ToString(), String.Join(',', x.SampleAliases.Skip(1)), x.enabled.ToString() }).ToArray(),
                new string[] { "Id", "Name", "PlayCount", "Aliases", "Enabled" });

            var pages = InteractivityExtension.GeneratePagesInEmbed(
                $"```Koekoe search result:\n\n{tableBuilder}```", SplitType.Line);
            await interactivity.SendPaginatedMessageAsync(ctx.Channel, ctx.User, pages);

            await ctx.EditResponseAsync("Done!");
        }

        [Command("samples"), System.ComponentModel.Description("List available samples")]
        public async Task Samples(CommandContext ctx)
        {
            await ctx.DeferResponseAsync();

            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            List<SampleData> samples = handler.GetGuildData().samples
                .Where(x => x.exists)
                .OrderBy(x => int.Parse(x.SampleAliases[0]))
                .ToList();

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
            string content = $"{header}\nAvailable Samples — use /play {{number}} to play.\n\n";

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
            }

            var interactivity = ctx.Client.ServiceProvider.GetRequiredService<InteractivityExtension>();
            var pages = InteractivityExtension.GeneratePagesInEmbed(content, SplitType.Line);
            await interactivity.SendPaginatedMessageAsync(ctx.Channel, ctx.User, pages,
                timeoutoverride: TimeSpan.FromMinutes(2));

            await ctx.EditResponseAsync("Done!");
        }

        [Command("updatesamples"), System.ComponentModel.Description("Update the list of available samples")]
        public async Task UpdateSamples(CommandContext ctx)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild);
            handler.UpdateSamplelist();
            await ctx.RespondAsync("Sample list updated");
        }

        [Command("play"), System.ComponentModel.Description("Play a sample — use /samples to see available samples")]
        public async Task PlaySample(
            CommandContext ctx,
            [System.ComponentModel.Description("sample number or alias from /samples")] string sampleNameOrAlias)
        {
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.ChannelId == null)
            {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }
            var voiceChannel = await vstat.GetChannelAsync();
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);

            SampleData sample = handler.getSample(sampleNameOrAlias);
            if (sample != null && sample.enabled)
            {
                await ctx.RespondAsync($"Playing `{sample.Name}`");
                handler.AnnounceSample(sampleNameOrAlias, 1, new List<DiscordChannel> { voiceChannel });
            }
            else
            {
                await ctx.RespondAsync($"{sampleNameOrAlias} {(sample == null ? "does not exist" : "is disabled")} :(");
            }
        }

        [Command("playfile"), System.ComponentModel.Description("DEBUG: Plays audio by filename.")]
        public async Task PlayFile(
            CommandContext ctx,
            [System.ComponentModel.Description("path to the file to play.")] string filename)
        {
            await ctx.DeferResponseAsync();

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

            // In v5 VoiceNext is resolved via DI
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
            }

            if (exc != null)
                await ctx.RespondAsync($"An exception occurred during playback: `{exc.GetType()}: {exc.Message}`");
            else
                await ctx.RespondAsync($"Finished playing `{filename}`");
        }
    }
}