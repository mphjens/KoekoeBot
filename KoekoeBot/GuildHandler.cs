﻿using DSharpPlus;
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
using Microsoft.Extensions.Logging;
using DSPlus.Examples;

namespace KoekoeBot
{
    public class AlarmData
    {
        public string AlarmName;
        public DateTime AlarmDate;
        public ulong userId; //The user that set this alarm, we will try to find this user in the guild's channels.
        public bool recurring; //Not implemented yet
    }

    public class GuildHandler
    {

        private List<DiscordChannel> Channels;
        public DiscordGuild Guild;

        // The minimum amount of time between playing bonus clips in minutes
        public int minBonusInterval = 60;
        // The max amount of minutes to be added to minBonusInterval
        public int variableBonusInterval = 30;

        public bool IsRunning { get; private set; }
        private DiscordClient Client;
        private bool ShouldRun;
        
        private SavedGuildData guildData;

        List<AlarmData> alarms;

        DateTime nextBonusClip = DateTime.UnixEpoch;
        Random rnd;

        public GuildHandler(DiscordClient client, DiscordGuild guild)
        {
            this.Channels = new List<DiscordChannel>();
            this.Client = client;
            this.Guild = guild;
            this.rnd = new Random();
            
            alarms = new List<AlarmData>();
        }

        public void Stop()
        {
            ShouldRun = false;
        }

        public void SetGuildData(SavedGuildData data){
            this.guildData = data;
        }
        public void UpdateSamplelist()
        {
            //Create sample list
            var data = this.guildData;
            data.samples = data.samples != null ? data.samples : new List<string>();
            string[] samplefiles = Directory.EnumerateFiles(Path.Combine(Environment.CurrentDirectory, "samples")).Where(x => x.EndsWith(".mp3")).ToArray();
            
            //todo: first use slots where the sample file does not exsist anymore.
            //      this way the indecies of exsisting samples don't change when
            //      samples are removed from the list. For now appending new ones
            //      works just fine.
            foreach (string sample in samplefiles)
            {
                //Add new samplefiles
                if (!guildData.samples.Contains(sample))
                {
                    guildData.samples.Add(sample);
                }
            }

            this.SaveGuildData();
        }

        public async Task Execute()
        {
            IsRunning = true;
            ShouldRun = true;

            //This loop ticks every new minute on the systemclock
            while (ShouldRun)
            {
                DateTime now = DateTime.Now;

                int alarmCountStart = alarms.Count;
                //Check if we need to trigger an alarm
                for (int i = 0; i < alarms.Count; i++)
                {
                    AlarmData alarm = alarms[i];

                    if (alarm.recurring && alarm.AlarmDate.DayOfWeek != now.DayOfWeek)
                        continue;
                    if (alarm.AlarmDate.Hour == now.Hour && alarm.AlarmDate.Minute == now.Minute)
                    {
                        System.Console.WriteLine($"Triggering alarm ${alarm.AlarmName} - {alarm.AlarmDate.ToShortTimeString()}");
                        //Announce in the channel where the user that set this alarm currently is.
                        await AnnounceFile(Path.Combine(Environment.CurrentDirectory, "samples", "CHIME1.wav"), 2, Channels.Where(x => x.Users.Where(x => x.Id == alarm.userId).Count() > 0).ToList());
                        alarms.RemoveAt(i); //Todo: implement recurring alarms
                        i--;
                    }
                }

                if (alarms.Count != alarmCountStart)
                {
                    SaveGuildData(); //save the alarms list if it has changed
                }

                //Special case for 420 (blaze it)
                if (now.Minute == 20 && (now.Hour % 12) == 4)
                {
                    await AnnounceFile(Path.Combine(Environment.CurrentDirectory, "samples", "420.mp3"));
                }

                if (now.Minute == 0) //If we entered a new hour
                {
                    System.Console.WriteLine($"Guildhandler entered new hour");
                    await AnnounceFile(getFileNameForHour(now.Hour));
                }

                //check for bonus clip
                if (nextBonusClip - now <= TimeSpan.Zero)
                {
                    //Play bonus clip
                    string[] extraClipFiles = Directory.EnumerateFiles(Path.Combine(Environment.CurrentDirectory, "samples")).Where(x => x.Contains("extra_")).ToArray();
                    int clipIndex = rnd.Next(extraClipFiles.Length);

                    if (nextBonusClip != DateTime.UnixEpoch)//Ignore the first time as this runs when the handler is started
                        await AnnounceFile(extraClipFiles[clipIndex]);

                    //Determine when we next will play a bonusclip (from minBonusInterval up to minBonusInterval + variableBonusInterval minutes)
                    nextBonusClip = DateTime.Now.AddMinutes(this.minBonusInterval + (int)(rnd.NextDouble() * this.variableBonusInterval));
                    System.Console.WriteLine($"Selected {extraClipFiles[clipIndex]} as bonus clip for {this.Guild.Name} which will be played at {nextBonusClip.ToShortTimeString()}");
                }

                //System.Console.WriteLine($"Guildhandler has ticked {Guild.Name}");
                //Calculate the number of miliseconds until a new minute on the system clock (fix? add one second to account for task.delay() inaccuracy)
                double millisToNextMinute = (double)((60 * 1000) - DateTime.Now.TimeOfDay.TotalMilliseconds % (60 * 1000));
                
                await Task.Delay((int)millisToNextMinute + 1000);
            }

            System.Console.WriteLine($"Guildhandler stopped {Guild.Name}");

            IsRunning = false;
        }

        //Joins, plays audio file and leaves again. for all registered channels in this guild
        public async Task AnnounceFile(string audio_path, int loopcount = 1, List<DiscordChannel> channels = null)
        {
            if (channels == null)
                channels = Channels;

            if (!File.Exists(audio_path))
            {
                System.Console.WriteLine($"Will not be playing {audio_path} (file not found)");
                return;
            }

            foreach (DiscordChannel Channel in Channels)
            {
                if (Channel.Users.Count() == 0)
                {
                    System.Console.WriteLine($"Will not be playing {audio_path} in {Channel.Guild.Name}/{Channel.Name} (no users online)");
                    continue; //skip empty channels
                }

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

                try
                {
                    // wait for current playback to finish
                    while (vnc.IsPlaying)
                    {
                        await vnc.WaitForPlaybackFinishAsync();
                    }

                    await vnc.SendSpeakingAsync(true);

                    await Task.Delay(500);

                    var psi = new ProcessStartInfo
                    {
                        FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "ffmpeg" : "ffmpeg.exe",
                        Arguments = $@"-i ""{audio_path}"" -ac 2 -f s16le -ar 48000 pipe:1 -loglevel quiet",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true

                    };

                    System.Console.WriteLine($"Playing {audio_path} in {Channel.Guild.Name}/{Channel.Name}");
                    //System.Console.WriteLine($"Will run {psi.FileName} as {psi.Arguments}");

                    for (int i = 0; i < loopcount; i++)
                    {
                        var ffmpeg = Process.Start(psi);
                        var ffout = ffmpeg.StandardOutput.BaseStream;

                        var txStream = vnc.GetTransmitSink();
                        await ffout.CopyToAsync(txStream);
                        await txStream.FlushAsync();
                        await vnc.WaitForPlaybackFinishAsync();
                    }

                    await Task.Delay(1000);
                }
                catch (Exception ex) { Console.Write(ex.StackTrace); }
                finally
                {
                    await vnc.SendSpeakingAsync(false);
                    vnext.GetConnection(Channel.Guild).Disconnect();
                }

            }
        }

        public void AddChannel(DiscordChannel channel, bool autosave = true)
        {
            if (!Channels.Contains(channel))
            {
                Channels.Add(channel);

                if (autosave)
                    this.SaveGuildData();
            }
        }

        public void AddAlarm(DateTime alarmdate, string alarmname, ulong userid, bool autosave = true)
        {
            AlarmData nAlarm = new AlarmData()
            {
                AlarmDate = alarmdate,
                AlarmName = alarmname,
                userId = userid
            };

            this.alarms.Add(nAlarm);

            if (autosave)
                this.SaveGuildData();
        }

        public List<AlarmData> GetAlarms()
        {
            return this.alarms;
        }

        public void RemoveChannel(DiscordChannel channel)
        {
            if (Channels.Remove(channel))
            {
                SaveGuildData();
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

        public SavedGuildData GetGuildData()
        {
            return this.guildData;
        }

        public void SaveGuildData()
        {
            SavedGuildData data = this.guildData != null ? this.guildData : new SavedGuildData();
            data.alarms = this.alarms.ToArray();
            data.channelIds = this.GetRegisteredChannelIds();
            System.Console.WriteLine($"Saving guild data {Guild.Name}");

            //Update saved data
            string data_path = Path.Combine(Environment.CurrentDirectory, "data", $"guilddata_{this.Guild.Id}.json");
            if (!File.Exists(data_path))
            {
                File.Create(data_path).Close();
            }
            File.WriteAllText(data_path, JsonConvert.SerializeObject(data));

            System.Console.WriteLine($"Saved guild data to {data_path}");
        }

        public void ClearGuildData()
        {
            this.alarms.Clear();
            this.Channels.Clear();

            //Delete saved data file
            string data_path = Path.Combine(Environment.CurrentDirectory, "data", $"guilddata_{this.Guild.Id}.json");
            if (!File.Exists(data_path))
            {
                File.Delete(data_path);
            }
        }

        private string getFileNameForHour(int hour)
        {
            string sampleDir = Path.Combine(Environment.CurrentDirectory, "samples");
            string[] allClips = Directory.EnumerateFiles(sampleDir).ToArray();
            string f = Path.GetRelativePath(sampleDir, allClips[0]);
            string[] hourClipFiles = allClips.Where(x => Path.GetRelativePath(sampleDir, x).StartsWith($"{hour % 12}_uur") && x.EndsWith(".mp3")).ToArray();
            int clipIndex = rnd.Next(hourClipFiles.Length);

            return hourClipFiles[clipIndex];
        }


    }
}
