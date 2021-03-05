using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;

namespace KoekoeBot
{
    public class AlarmData
    {
        public string AlarmName;
        public DateTime AlarmDate;
        public bool recurring;
    }

    public class GuildHandler
    {

        private List<DiscordChannel> Channels;
        public DiscordGuild Guild;
        public bool IsRunning { get; private set; }
        private DiscordClient Client;
        private bool ShouldRun;

        List<AlarmData> alarms;

        DateTime nextBonusClip = DateTime.UnixEpoch;

        public GuildHandler(DiscordClient client, DiscordGuild guild)
        {
            this.Channels = new List<DiscordChannel>();
            this.Client = client;
            this.Guild = guild;

            alarms = new List<AlarmData>();
        }

        public void Stop()
        {
            ShouldRun = false;
        }

        public async Task Execute()
        {
            IsRunning = true;
            ShouldRun = true;

            //This loop ticks every new minute on the systemclock
            while (ShouldRun)
            {
                DateTime now = DateTime.Now;

                //Debug: Chime twice every minute
                //await AnnounceFile(Path.Combine(Environment.CurrentDirectory, "samples", "CHIME1.wav"), 2);

                //Check if we need to trigger an alarm
                foreach (AlarmData alarm in alarms)
                {
                    if (alarm.recurring && alarm.AlarmDate.DayOfWeek != now.DayOfWeek)
                        continue;
                    if (alarm.AlarmDate.Hour == now.Hour && alarm.AlarmDate.Minute == now.Minute)
                    {
                        await AnnounceFile(Path.Combine(Environment.CurrentDirectory, "samples", "CHIME1.wav"), 2);
                    }

                }

                //Special case for 420 (blaze it)
                if (now.Minute == 20 && (now.Hour % 12) == 4)
                {
                    await AnnounceFile(Path.Combine(Environment.CurrentDirectory, "samples", "420.mp3"));
                }

                if (now.Minute == 0) //If we entered a new hour
                {
                    await AnnounceFile(getFileNameForHour(now.Hour));
                }

                //check for bonus clip
                if (nextBonusClip - now <= TimeSpan.Zero)
                {
                    //Play bonus clip
                    Random rnd = new Random();
                    string[] extraClipFiles = Directory.EnumerateFiles(Path.Combine(Environment.CurrentDirectory, "samples")).Where(x=>x.StartsWith("extra_")).ToArray();
                    int clipIndex = (int)Math.Round((double)rnd.NextDouble() * (extraClipFiles.Length - 1));

                    if (nextBonusClip != DateTime.UnixEpoch)//Ignore the first time as this runs when the handler is started
                        await AnnounceFile(Path.Combine(Environment.CurrentDirectory, "samples", extraClipFiles[clipIndex]));


                    //Determine when we next will play a bonusclip (after 2h and a random amount of minutes)
                    nextBonusClip = DateTime.Now.AddHours(2).AddMinutes((int)(rnd.NextDouble() * 60f));
                }

                //Calculate the number of miliseconds until a new minute on the system clock
                double millisToNextMinute = (double)((60 * 1000) - DateTime.Now.TimeOfDay.TotalMilliseconds % (60 * 1000));
                await Task.Delay((int)millisToNextMinute);
            }

            IsRunning = false;
        }
        public async Task AnnounceFile(string audio_path, int loopcount = 1)
        {
            foreach (DiscordChannel Channel in Channels)
            {
                if (Channel.Users.Count() == 0)
                    continue; //skip empty channels

                // check whether VNext is enabled
                var vnext = Client.GetVoiceNext();
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


                try
                {
                    await vnc.SendSpeakingAsync(true);

                    var psi = new ProcessStartInfo
                    {
                        FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "ffmpeg" : "ffmpeg.exe",
                        Arguments = $@"-i ""{audio_path}"" -ac 2 -f s16le -ar 48000 pipe:1 -loglevel quiet",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true

                    };

                    System.Console.WriteLine($"Will run {psi.FileName} as {psi.Arguments}");

                    for (int i = 0; i < loopcount; i++)
                    {
                        var ffmpeg = Process.Start(psi);
                        var ffout = ffmpeg.StandardOutput.BaseStream;

                        var txStream = vnc.GetTransmitSink();
                        await ffout.CopyToAsync(txStream);
                        await txStream.FlushAsync();
                        await vnc.WaitForPlaybackFinishAsync();
                    }
                }
                catch (Exception ex) { Console.Write(ex.StackTrace); }
                finally
                {
                    await vnc.SendSpeakingAsync(false);
                    vnext.GetConnection(Channel.Guild).Disconnect();
                }

            }
        }

        public void AddChannel(DiscordChannel channel)
        {
            if (!Channels.Contains(channel))
            {
                Channels.Add(channel);

                //Update saved data
                string data_path = Path.Combine(Environment.CurrentDirectory, "data", $"guilddata_{this.Guild.Id}.json");
                if (!File.Exists(data_path))
                {
                    File.Create(data_path).Close();
                }
                File.WriteAllText(data_path, JsonConvert.SerializeObject(this.GetRegisteredChannelIds()));

                //System.Console.WriteLine($"Added channel to list {channel.Name}");
            }
        }

        public void RemoveChannel(DiscordChannel channel)
        {
            if (Channels.Remove(channel))
            {
                //Update our data file
                string data_path = Path.Combine(Environment.CurrentDirectory, "data", $"guilddata_{this.Guild.Id}.json");
                if (!File.Exists(data_path))
                {
                    File.Create(data_path).Close();
                }
                File.WriteAllText(data_path, JsonConvert.SerializeObject(this.GetRegisteredChannelIds()));

            }
        }

        public string[] GetRegisteredChannelNames()
        {
            return this.Channels.Select(x => x.Name).ToArray();
        }

        public ulong[] GetRegisteredChannelIds()
        {
            return this.Channels.Select(x => x.Id).ToArray();
        }


        private string getFileNameForHour(int hour)
        {
            return Path.Combine(Environment.CurrentDirectory, "samples", $"{hour % 12}_uur.mp3");
        }

        //Joins, plays audio file and leaves again. for all registered channels in this guild
    }
}
