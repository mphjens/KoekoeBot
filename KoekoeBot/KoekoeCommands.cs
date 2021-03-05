using System;
using System.Collections.Generic;
using System.Text;

namespace KoekoeBot
{

    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using DSharpPlus.CommandsNext;
    using DSharpPlus.CommandsNext.Attributes;
    using DSharpPlus.Entities;
    using DSharpPlus.VoiceNext;
    using Newtonsoft.Json;

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
            if(handler != null)
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
            if(handler != null)
            {
                string channelstext = String.Join("`, `", handler.GetRegisteredChannelNames());

                await ctx.RespondAsync($"Currently registered to: `{channelstext}`");
            }


            await ctx.RespondAsync($"Currently not registered to any channel, use `!kk register` while in a voice channel to add it.");
        }


        [Command("setalarm"), Description("set an alarm for your current voicechannel")]
        public async Task SetAlarm(CommandContext ctx, [RemainingText, Description("Alarm time ex 4:20 or 15:34")] string datestring)
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
            if(datestringComps.Length == 2)
            {
                int parsedHours, parsedMinutes;
                if(int.TryParse(datestringComps[0], out parsedHours) && int.TryParse(datestringComps[1], out parsedMinutes))
                {
                    int hourdiff = (parsedHours - DateTime.Now.Hour) % 24;
                    if (hourdiff < 0)
                        hourdiff += 24;

                    int mindiff = (parsedMinutes - DateTime.Now.Minute) % 60;
                    DateTime dt = DateTime.Now.AddHours(hourdiff).AddMinutes(mindiff);

                    GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, vstat.Channel.Guild, true);
                    
                    //TODO: Add alarm to handler here

                    if (!handler.IsRunning) //If the handler isn't running for some reason start it TODO: remove these, or implement handler sleep 
                        handler.Execute(); //Will run async
                }
                
            }

            await ctx.RespondAsync($"Registered alarm to `{vstat.Channel.Name}`");
        }


        //TODO: remove or maybe limit these debug commands

        [Command("Announce"), Description("DEBUG: Announces an audio file to all registered channels in the sender's guild")]
        public async Task Announce(CommandContext ctx, [RemainingText, Description("path to the file to play.")] string filename)
        {
            GuildHandler handler = KoekoeController.GetGuildHandler(ctx.Client, ctx.Guild, true);
            if(handler != null)
            {
                await ctx.RespondAsync($"Will announce `{filename}`");
                await handler.AnnounceFile(filename);
                await ctx.RespondAsync($"Done announcing `{filename}`");
            }
            
        }

        //Used for debugging the voicenext and ffmpeg stuff
        [Command("play"), Description("DEBUG: Plays an audio file.")]
        public async Task Play(CommandContext ctx, [RemainingText, Description("path to the file to play.")] string filename)
        {
            // get member's voice state
            var vstat = ctx.Member?.VoiceState;
            if (vstat?.Channel == null)
            {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
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

